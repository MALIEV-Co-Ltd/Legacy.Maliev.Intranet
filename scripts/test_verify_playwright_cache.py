import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("cache_gate", Path(__file__).with_name("verify-playwright-cache.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class CacheGateTests(unittest.TestCase):
    def fixture(self, root, declared="1.61.0"):
        package = root / "package"
        package.mkdir()
        (package / "package.json").write_text(json.dumps({"version": "1.61.1-beta-1782139630000"}))
        (package / "browsers.json").write_text(json.dumps({"browsers": [
            {"name": "chromium", "revision": "1228", "browserVersion": "149.0.7827.55"},
            {"name": "chromium-headless-shell", "revision": "1228", "browserVersion": "149.0.7827.55"},
            {"name": "ffmpeg", "revision": "1011"}]}))
        project = root / "tests.csproj"
        project.write_text('<Project><ItemGroup><PackageReference Include="Microsoft.Playwright.Xunit" Version="' + declared + '" /></ItemGroup></Project>')
        restored = root / "nuget/microsoft.playwright/1.61.0/.playwright/package"
        restored.mkdir(parents=True)
        for name in ("package.json", "browsers.json"):
            (restored / name).write_bytes((package / name).read_bytes())
        (root / "obj").mkdir()
        libraries = {name + "/1.61.0": {"type": "package", "path": name.lower() + "/1.61.0"}
                     for name in ("Microsoft.Playwright", "Microsoft.Playwright.Xunit", "Microsoft.Playwright.TestAdapter")}
        (root / "obj/project.assets.json").write_text(json.dumps({"libraries": libraries, "targets": {"net10.0": libraries}, "packageFolders": {str(root / "nuget"): {}}}))
        return package, project

    def populated(self, root):
        value = gate.metadata(*self.fixture(root), "ubuntu24.04-x64")
        cache = root / "intranet-playwright"
        for browser in value["browsers"].values():
            path = cache / browser["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("controlled cache fixture")
            path.chmod(0o755)
        return cache, value

    def test_exact_metadata_and_both_browser_versions_are_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            cache, value = self.populated(Path(temporary))
            self.assertEqual("1.61.0", value["packageVersion"])
            self.assertEqual("1.61.1-beta-1782139630000", value["driverVersion"])
            self.assertEqual(64, len(value["browserFingerprint"]))
            calls = []
            def probe(path, argument):
                calls.append((path.name, argument))
                return "ffmpeg version n4.3.1" if path.name == "ffmpeg-linux" else "Google Chrome for Testing 149.0.7827.55"
            self.assertTrue(gate.validate_cache(cache, value, probe))
            self.assertEqual(3, len(calls))

    def test_driver_project_version_mismatch_is_not_a_cache_hit(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaisesRegex(ValueError, "pinned project"):
                gate.metadata(*self.fixture(Path(temporary), "1.60.0"), "ubuntu24.04-x64")

    def test_resolved_nuget_version_mismatch_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package, project = self.fixture(root)
            assets = json.loads((root / "obj/project.assets.json").read_text())
            assets["libraries"]["Microsoft.Playwright/1.60.0"] = assets["libraries"].pop("Microsoft.Playwright/1.61.0")
            (root / "obj/project.assets.json").write_text(json.dumps(assets))
            with self.assertRaisesRegex(ValueError, "Resolved NuGet"):
                gate.metadata(package, project, "ubuntu24.04-x64")

    def test_central_or_expression_version_cannot_bypass_explicit_pin(self):
        with tempfile.TemporaryDirectory() as temporary:
            package, project = self.fixture(Path(temporary))
            for version in (None, "$(PlaywrightVersion)"):
                attribute = '' if version is None else ' Version="' + version + '"'
                project.write_text('<Project><ItemGroup><PackageReference Include="Microsoft.Playwright.Xunit"' + attribute + ' /></ItemGroup></Project>')
                with self.assertRaisesRegex(ValueError, "Explicit pinned"):
                    gate.metadata(package, project, "ubuntu24.04-x64")

    def test_stale_built_metadata_or_unreviewed_driver_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            package, project = self.fixture(Path(temporary))
            raw = (package / "browsers.json").read_bytes()
            (package / "browsers.json").write_bytes(raw + b' ')
            with self.assertRaisesRegex(ValueError, "differs from exact resolved"):
                gate.metadata(package, project, "ubuntu24.04-x64")
            (package / "package.json").write_text(json.dumps({"version": "1.61.1-beta-unknown"}))
            with self.assertRaisesRegex(ValueError, "reviewed pinned"):
                gate.metadata(package, project, "ubuntu24.04-x64")

    def test_missing_headless_shell_or_wrong_browser_version_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cache, value = self.populated(Path(temporary))
            self.assertFalse(gate.validate_cache(cache, value, lambda *args: "Google Chrome 149.0.7827.550"))
            (cache / value["browsers"]["chromium-headless-shell"]["path"]).unlink()
            self.assertFalse(gate.validate_cache(cache, value, lambda *args: "Google Chrome 149.0.7827.55"))

    def test_nonexecutable_and_probe_timeout_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cache, value = self.populated(Path(temporary))
            with patch.object(gate.os, "access", return_value=False):
                self.assertFalse(gate.validate_cache(cache, value))
            def timeout(*args):
                raise TimeoutError("Actual probe lease model")
            self.assertFalse(gate.validate_cache(cache, value, timeout))

    def test_ambiguous_metadata_and_unreviewed_host_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            package, project = self.fixture(Path(temporary))
            with self.assertRaisesRegex(ValueError, "host platform"):
                gate.metadata(package, project, "windows-x64")
            value = json.loads((package / "browsers.json").read_text())
            value["browsers"].append(value["browsers"][0])
            (package / "browsers.json").write_text(json.dumps(value))
            (Path(temporary) / "nuget/microsoft.playwright/1.61.0/.playwright/package/browsers.json").write_bytes((package / "browsers.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "ambiguous"):
                gate.metadata(package, project, "ubuntu24.04-x64")

    def test_cleanup_only_removes_exact_private_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cache, value = self.populated(root)
            unrelated = root / "unrelated"
            unrelated.mkdir()
            with self.assertRaisesRegex(ValueError, "exact runner-private"):
                gate.discard_private_cache(unrelated, root)
            gate.discard_private_cache(cache, root)
            self.assertFalse(cache.exists())
            self.assertTrue(unrelated.exists())

    def test_version_probe_reaps_actual_success_and_timeout_children(self):
        # No browser/SDK launched: two exact tiny Python processes exercise ownership.
        with tempfile.TemporaryDirectory() as temporary:
            fake = Path(temporary) / "unused"
            children = []
            popen = gate.subprocess.Popen
            def child(arguments, **kwargs):
                import sys
                body = "print('version')" if arguments[1] == "success" else "import time;time.sleep(60)"
                process = popen([sys.executable, "-c", body], **kwargs)
                children.append(process)
                return process
            with patch.object(gate.subprocess, "Popen", side_effect=child):
                self.assertEqual("version", gate.version_output(fake, "success").strip())
                with patch.object(gate.time, "monotonic", side_effect=[0, 11]), self.assertRaises(TimeoutError):
                    gate.version_output(fake, "timeout")
                # Cancellation of terminate must not skip kill, wait or reader closure.
                with patch.object(gate.time, "monotonic", side_effect=[0, 11]), patch.object(popen, "terminate", side_effect=KeyboardInterrupt), self.assertRaises(TimeoutError):
                    gate.version_output(fake, "timeout")
            self.assertEqual(3, len(children))
            for process in children:
                self.assertIsNotNone(process.poll())
                self.assertTrue(process.stdout.closed)


if __name__ == "__main__":
    unittest.main()
