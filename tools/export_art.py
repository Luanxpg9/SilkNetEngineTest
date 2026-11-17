"""Exports every SVG in the games' Assets folders to a PNG next to it.

The SVG files are the editable sources (Inkscape opens them); the PNGs are what the game loads.
Rendering uses Microsoft Edge (or Chrome) in headless mode, which every Windows PC already has:
the browser opens the SVG at its exact width and height on a transparent background and saves a
screenshot. No extra packages are needed.

Run from anywhere:
    python tools/export_art.py             # all SVGs
    python tools/export_art.py rifle       # only files whose name contains "rifle"
"""
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BROWSERS = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
]


def find_browser() -> Path:
    for browser in BROWSERS:
        if browser.exists():
            return browser
    sys.exit("Couldn't find Edge or Chrome; edit BROWSERS in tools/export_art.py")


def svg_size(svg: Path) -> tuple[int, int]:
    """Reads width and height (in pixels) from the root <svg> element."""
    head = svg.read_text(encoding="utf-8")[:2000]
    root = re.search(r"<svg\b[^>]*>", head, re.S)
    width = re.search(r'\bwidth="(\d+)', root.group(0)) if root else None
    height = re.search(r'\bheight="(\d+)', root.group(0)) if root else None
    if not (width and height):
        sys.exit(f"{svg}: the <svg> element needs width and height in pixels")
    return int(width.group(1)), int(height.group(1))


def export(browser: Path, svg: Path, profile: str) -> None:
    width, height = svg_size(svg)
    png = svg.with_suffix(".png")
    subprocess.run(
        [
            str(browser),
            "--headless",
            "--disable-gpu",
            "--hide-scrollbars",
            f"--user-data-dir={profile}",          # a throwaway profile, so the user's browser isn't touched
            "--force-device-scale-factor=1",       # one SVG pixel = one PNG pixel, whatever the screen's scaling
            "--default-background-color=00000000", # transparent instead of white
            f"--window-size={width},{height}",
            f"--screenshot={png}",
            svg.as_uri(),
        ],
        check=True,
        capture_output=True,
    )
    print(f"{svg.relative_to(ROOT)} -> {png.name} ({width}x{height})")


def main() -> None:
    name_filter = sys.argv[1] if len(sys.argv) > 1 else ""
    svgs = [svg for svg in sorted(ROOT.glob("src/*/Assets/**/*.svg")) if name_filter in svg.stem]
    if not svgs:
        sys.exit("No SVG files matched")

    browser = find_browser()
    with tempfile.TemporaryDirectory() as profile:
        for svg in svgs:
            export(browser, svg, profile)


if __name__ == "__main__":
    main()
