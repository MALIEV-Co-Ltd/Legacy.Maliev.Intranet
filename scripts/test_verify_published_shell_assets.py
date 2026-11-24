import runpy
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest


verify = runpy.run_path(str(Path(__file__).with_name("verify-published-shell-assets.py")))["verify"]
INDEX = """<base href="/">
<link rel="stylesheet" href="css/app.css">
<link rel="preload" href="_framework/blazor.webassembly.js">
<img src="images/logo.svg" alt="">
<script src="https://example.test/external.js"></script>
<script src="js/forms.js?v=1"></script>
<script src="_framework/blazor.webassembly.js"></script>
"""


class PublishedShellAssetsTests(unittest.TestCase):
    def setUp(self):
        scratch = TemporaryDirectory()
        self.addCleanup(scratch.cleanup)
        self.root = Path(scratch.name)
        for name in ("css/app.css", "images/logo.svg", "js/forms.js", "_framework/blazor.webassembly.js"):
            path = self.root / name
            path.parent.mkdir(exist_ok=True)
            path.write_text("synthetic", encoding="utf-8")

    def check(self, html):
        (self.root / "index.html").write_text(html, encoding="utf-8")
        verify(self.root)

    def test_local_files_and_boot_order(self):
        self.check(INDEX)

    def test_missing_local_stylesheet(self):
        (self.root / "css/app.css").unlink()
        with self.assertRaisesRegex(ValueError, "Missing local stylesheet"):
            self.check(INDEX)

    def test_duplicate_script_with_different_url_spelling(self):
        extra = '<script src="/js/%66orms.js?v=2"></script>'
        with self.assertRaisesRegex(ValueError, "Duplicate script"):
            self.check(INDEX.replace('<script src="js/forms.js?v=1">', extra + '<script src="js/forms.js?v=1">'))

    def test_boot_before_form_script(self):
        boot = '<script src="_framework/blazor.webassembly.js"></script>'
        with self.assertRaisesRegex(ValueError, "must follow"):
            self.check(INDEX.replace(boot, "").replace("<img ", boot + "<img "))

    def test_encoded_parent_traversal_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Invalid local script"):
            self.check(INDEX.replace("js/forms.js", "%2e%2e/forms.js"))


if __name__ == "__main__":
    unittest.main()
