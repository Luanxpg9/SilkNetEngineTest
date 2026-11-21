"""Run one of this repo's games and drive its window: keys, mouse, screenshots, log.

Windows only (it uses user32 through ctypes). Needs Pillow for screenshots.

    python .claude/skills/run-game/drive.py [--build] [--project TopDownShooter] [--shots DIR] ACTION...

Actions run in order:
    wait:SECONDS            sleep
    key:NAME                tap a key (A, F3, ESC, SPACE, LEFT, ...)
    hold:NAME+NAME:SECONDS  hold keys together, then release (hold:W+A:1.5)
    aim:WX,WY               put the mouse WX, WY world units from the window's center (y up),
                            at --ppu pixels per unit (90 for the shooter's camera)
    pixel:X,Y               put the mouse at client pixel X, Y (y down, from the top left)
    click[:COUNT[:GAP]]     left clicks, GAP seconds apart (click:10:0.05)
    shot:NAME               save a screenshot of the window to DIR/NAME.png

The game is closed with WM_CLOSE (so its Unload runs) and its console output is printed,
so F3 lines and GL errors show up. Exit code: the game's.
"""
import argparse
import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

TITLES = {
    "TopDownShooter": "HavingFunWithEngine - Top-Down Shooter",
    "Sandbox": "HavingFunWithEngine - Sandbox",
}

VK = {"ESC": 0x1B, "SPACE": 0x20, "ENTER": 0x0D, "TAB": 0x09, "SHIFT": 0x10, "CTRL": 0x11,
      "LEFT": 0x25, "UP": 0x26, "RIGHT": 0x27, "DOWN": 0x28}
VK.update({f"F{i}": 0x6F + i for i in range(1, 13)})
VK.update({chr(c): c for c in range(ord("A"), ord("Z") + 1)})
VK.update({str(d): 0x30 + d for d in range(10)})

user32 = ctypes.windll.user32
KEYUP, MOUSE_DOWN, MOUSE_UP, WM_CLOSE = 0x2, 0x2, 0x4, 0x0010


def vk(name):
    try:
        return VK[name.upper()]
    except KeyError:
        sys.exit(f"unknown key '{name}'; known: {', '.join(sorted(VK))}")


def find_window(title, timeout):
    end = time.time() + timeout
    while time.time() < end:
        hwnd = user32.FindWindowW(None, title)
        if hwnd:
            return hwnd
        time.sleep(0.05)
    return 0


def client_origin(hwnd):
    pt = wintypes.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    rect = wintypes.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rect))
    return pt.x, pt.y, rect.right, rect.bottom


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default="TopDownShooter", help="folder under src/ (default TopDownShooter)")
    parser.add_argument("--title", help="window title, if the project isn't one this script knows")
    parser.add_argument("--build", action="store_true", help="dotnet build the solution first")
    parser.add_argument("--shots", default=".", help="folder for screenshots (use the scratchpad)")
    parser.add_argument("--ppu", type=float, default=90.0, help="pixels per world unit for aim: (default 90)")
    parser.add_argument("actions", nargs="*")
    args = parser.parse_args()

    root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
    if args.build:
        build = subprocess.run(["dotnet", "build", os.path.join(root, "HavingFunWithEngine.sln"), "-nologo", "-v", "q"],
                               capture_output=True, text=True)
        tail = [l for l in build.stdout.splitlines() if "Warning(s)" in l or "Error(s)" in l or ": error " in l or ": warning " in l]
        print("\n".join(tail))
        if build.returncode != 0:
            sys.exit("build failed")

    dll = os.path.join(root, "src", args.project, "bin", "Debug", "net9.0", f"{args.project}.dll")
    if not os.path.exists(dll):
        sys.exit(f"not built: {dll} (pass --build)")
    title = args.title or TITLES.get(args.project)
    if not title:
        sys.exit(f"pass --title for project '{args.project}'")

    game = subprocess.Popen(["dotnet", dll], cwd=os.path.dirname(dll),
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    hwnd = find_window(title, timeout=20)
    if not hwnd:
        game.kill()
        print(game.communicate()[0])
        sys.exit(f"no window titled '{title}' appeared")
    time.sleep(0.4)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.2)
    ox, oy, width, height = client_origin(hwnd)
    os.makedirs(args.shots, exist_ok=True)

    for action in args.actions:
        name, _, rest = action.partition(":")
        name = name.lower()
        if name == "wait":
            time.sleep(float(rest))
        elif name == "key":
            code = vk(rest)
            user32.keybd_event(code, 0, 0, 0); time.sleep(0.05)
            user32.keybd_event(code, 0, KEYUP, 0); time.sleep(0.1)
        elif name == "hold":
            keys, _, seconds = rest.rpartition(":")
            codes = [vk(k) for k in keys.split("+")]
            for code in codes:
                user32.keybd_event(code, 0, 0, 0)
            time.sleep(float(seconds))
            for code in codes:
                user32.keybd_event(code, 0, KEYUP, 0)
            time.sleep(0.1)
        elif name == "aim":
            wx, wy = (float(v) for v in rest.split(","))
            user32.SetCursorPos(int(ox + width / 2 + wx * args.ppu), int(oy + height / 2 - wy * args.ppu))
            time.sleep(0.1)
        elif name == "pixel":
            x, y = (int(v) for v in rest.split(","))
            user32.SetCursorPos(ox + x, oy + y)
            time.sleep(0.1)
        elif name == "click":
            parts = rest.split(":") if rest else []
            count = int(parts[0]) if parts else 1
            gap = float(parts[1]) if len(parts) > 1 else 0.1
            for _ in range(count):
                user32.mouse_event(MOUSE_DOWN, 0, 0, 0, 0); time.sleep(0.02)
                user32.mouse_event(MOUSE_UP, 0, 0, 0, 0); time.sleep(gap)
        elif name == "shot":
            from PIL import ImageGrab
            path = os.path.join(args.shots, f"{rest}.png")
            ImageGrab.grab(bbox=(ox, oy, ox + width, oy + height)).save(path)
            print(f"screenshot: {os.path.abspath(path)}")
        else:
            sys.exit(f"unknown action '{action}'")

    user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    try:
        output = game.communicate(timeout=15)[0]
    except subprocess.TimeoutExpired:
        game.kill()
        output = game.communicate()[0] + "\n(killed: it didn't close within 15 s)"
    print(output)
    sys.exit(game.returncode or 0)


if __name__ == "__main__":
    main()
