"""Verify direct Intranet shell asset references in an existing publish wwwroot."""

import argparse
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlsplit


BOOT_SCRIPT = "_framework/blazor.webassembly.js"


class ShellAssets(HTMLParser):
    def __init__(self):
        super().__init__()
        self.references = []
        self.bases = []

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag == "base":
            self.bases.append(attrs.get("href"))
        elif tag == "script" and attrs.get("src"):
            self.references.append(("script", attrs["src"]))
        elif tag == "img" and attrs.get("src"):
            self.references.append(("image", attrs["src"]))
        elif tag == "link" and attrs.get("href"):
            relations = set((attrs.get("rel") or "").lower().split())
            for role in sorted(relations & {"stylesheet", "icon", "preload", "modulepreload"}):
                self.references.append((role, attrs["href"]))


def verify(wwwroot: Path) -> None:
    root = wwwroot.resolve(strict=True)
    shell = ShellAssets()
    shell.feed((root / "index.html").read_text(encoding="utf-8-sig"))
    shell.close()
    if shell.bases != ["/"]:
        raise ValueError("Expected one root-relative shell base")

    seen = set()
    scripts = []
    for role, reference in shell.references:
        url = urlsplit(reference)
        if url.scheme or url.netloc:
            continue
        path = unquote(url.path)
        name = PurePosixPath(path.lstrip("/"))
        if not path or not name.parts or ".." in name.parts or "\\" in path:
            raise ValueError(f"Invalid local {role} reference")
        key = (role, name)
        if key in seen:
            raise ValueError(f"Duplicate {role}: {name}")
        seen.add(key)
        asset = (root / name).resolve()
        if not asset.is_relative_to(root) or not asset.is_file():
            raise ValueError(f"Missing local {role}: {name}")
        if role == "script":
            scripts.append(name.as_posix())

    if not scripts or scripts[-1] != BOOT_SCRIPT:
        raise ValueError("Blazor boot script must follow all local shell scripts")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wwwroot", type=Path, help="Local publish wwwroot to inspect")
    args = parser.parse_args()
    try:
        verify(args.wwwroot)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print("Published shell asset references verified")


if __name__ == "__main__":
    main()
