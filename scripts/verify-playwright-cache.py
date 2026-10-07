"""Validate exact Playwright driver metadata and its private Linux browser cache."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET


def metadata(package, project, host):
    if not re.fullmatch(r"ubuntu(20|22|24|26)\.04-x64", host):
        raise ValueError("Unreviewed Playwright host platform")
    declared = [node.attrib.get("Version") for node in ET.parse(project).iter("PackageReference")
                if node.attrib.get("Include") == "Microsoft.Playwright.Xunit"]
    version = json.loads((package / "package.json").read_text())["version"]
    if declared != [version] or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Built Playwright driver does not match pinned project package")
    raw = (package / "browsers.json").read_bytes()
    values = json.loads(raw)["browsers"]
    browsers = {}
    for name in ("chromium", "chromium-headless-shell", "ffmpeg"):
        selected = [value for value in values if value["name"] == name]
        if len(selected) != 1:
            raise ValueError("Required browser metadata is missing or ambiguous")
        value = selected[0]
        override = value.get("revisionOverrides", {}).get(host)
        revision = override or value["revision"]
        if not re.fullmatch(r"\d+", str(revision)):
            raise ValueError("Invalid browser revision")
        directory = name.replace("-", "_") + "-" + str(revision)
        if override:
            directory = (name + "_" + host + "_special").replace("-", "_") + "-" + str(revision)
        browser_version = value.get("browserVersion")
        if name != "ffmpeg" and not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", browser_version or ""):
            raise ValueError("Required browser version unavailable")
        binaries = {"chromium": "chrome-linux64/chrome",
                    "chromium-headless-shell": "chrome-headless-shell-linux64/chrome-headless-shell",
                    "ffmpeg": "ffmpeg-linux"}
        browsers[name] = {"path": directory + "/" + binaries[name], "version": browser_version}
    return {"packageVersion": version, "browserFingerprint": hashlib.sha256(raw).hexdigest(),
            "hostPlatform": host, "browsers": browsers}


def version_output(executable, argument):
    # Keep the exact child handle through failure, with bounded output and no workers.
    process = subprocess.Popen([str(executable), argument], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    data = bytearray()
    failure = None
    try:
        os.set_blocking(process.stdout.fileno(), False)
        deadline = time.monotonic() + 10
        while True:
            if time.monotonic() >= deadline:
                raise TimeoutError("Cached browser version probe exceeded ten seconds")
            try:
                chunk = os.read(process.stdout.fileno(), 4096)
            except BlockingIOError:
                time.sleep(0.01)
                continue
            if not chunk:
                break
            if len(data) + len(chunk) > 4096:
                raise ValueError("Cached browser version output exceeded budget")
            data.extend(chunk)
        if process.wait(timeout=max(0.01, deadline - time.monotonic())) != 0:
            raise ValueError("Cached browser executable failed")
    except BaseException as error:
        failure = error
    finally:
        # Never drop a live/unknown owned child before deleting any cache directory.
        exited = False
        while not exited or not process.stdout.closed:
            try:
                exited = process.poll() is not None
            except BaseException as error:
                failure = failure or error
                exited = False
            if exited:
                try:
                    process.stdout.close()
                except BaseException as error:
                    failure = failure or error
                if process.stdout.closed:
                    break
            for action in (process.terminate, process.kill):
                if exited:
                    break
                try:
                    action()
                except BaseException as error:
                    failure = failure or error
                try:
                    process.wait(timeout=2)
                except BaseException as error:
                    failure = failure or error
                try:
                    exited = process.poll() is not None
                except BaseException as error:
                    failure = failure or error
                    exited = False
                if exited:
                    break
            try:
                time.sleep(0.05)
            except BaseException as error:
                failure = failure or error
    if failure is not None:
        raise failure
    return data.decode("utf-8", errors="strict")


def validate_cache(cache, value, probe=version_output):
    if cache.is_symlink():
        return False
    for name, browser in value["browsers"].items():
        path = cache / browser["path"]
        if not path.is_file() or not os.access(path, os.X_OK):
            return False
        if any(parent.is_symlink() for parent in [path] + list(path.parents)[:len(Path(browser["path"]).parts)]):
            return False
        try:
            output = probe(path, "-version" if name == "ffmpeg" else "--version")
        except (OSError, ValueError, TimeoutError):
            return False
        if name == "ffmpeg":
            if "ffmpeg version " not in output:
                return False
        elif re.search(r"(?<![\d.])" + re.escape(browser["version"]) + r"(?![\d.])", output) is None:
            return False
    return True


def discard_private_cache(cache, parent):
    expected = parent.resolve(strict=True) / "intranet-playwright"
    if cache.is_symlink() or cache.resolve() != expected or cache.name != "intranet-playwright":
        raise ValueError("Cache cleanup target is outside the exact runner-private directory")
    if cache.exists():
        shutil.rmtree(cache)


def host_platform():
    values = dict(line.strip().split("=", 1) for line in Path("/etc/os-release").read_text().splitlines() if "=" in line)
    if values.get("ID", "").strip('"') != "ubuntu" or os.environ.get("RUNNER_ARCH") != "X64":
        raise ValueError("This browser cache gate requires the reviewed Ubuntu X64 runner")
    return "ubuntu" + values["VERSION_ID"].strip('"') + "-x64"


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--cache", type=Path)
    parser.add_argument("--cache-parent", type=Path)
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--require", action="store_true")
    parser.add_argument("--discard-invalid", action="store_true")
    args = parser.parse_args()
    value = metadata(args.package, args.project, host_platform())
    valid = validate_cache(args.cache, value) if args.cache else None
    if valid is False and args.discard_invalid:
        if args.cache_parent is None:
            raise ValueError("Exact cache cleanup parent required")
        discard_private_cache(args.cache, args.cache_parent)
    if args.github_output:
        with args.github_output.open("a") as output:
            output.write("package-version=" + value["packageVersion"] + "\n")
            output.write("browser-fingerprint=" + value["browserFingerprint"] + "\n")
            output.write("host-platform=" + value["hostPlatform"] + "\n")
            if valid is not None:
                output.write("valid=" + str(valid).lower() + "\n")
    print(json.dumps({"packageVersion": value["packageVersion"], "hostPlatform": value["hostPlatform"],
                      "browserFingerprint": value["browserFingerprint"], "cacheValid": valid}, sort_keys=True))
    if args.require and valid is not True:
        raise SystemExit("Expected Chromium, headless shell and FFmpeg executable/version validation failed")
