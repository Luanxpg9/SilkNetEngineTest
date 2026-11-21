---
name: run-game
description: Build and run this repo's games (TopDownShooter, Sandbox) and drive their window from a script, to check a change really works - press keys, hold movement, aim and click, take screenshots of the window, read the console log (F3 stats, GL errors). Use after any change to the engine or a game, before calling it done, and to measure numbers the docs quote.
---

# Running and driving the games

You can't see the window, so check changes by driving it with `drive.py` (next to this file) and reading what comes back: the console log and screenshots of the window.

```bash
python .claude/skills/run-game/drive.py --build --shots "<scratchpad>/shots" \
  key:F2 shot:start hold:W+A:1.5 shot:walked aim:3,2 click:10:0.06 key:F3 wait:0.5
```

- `--build` builds the solution first and prints the warning and error counts (the target is 0 and 0).
- `--project Sandbox` for the Sandbox; the default is `TopDownShooter`.
- Put screenshots in the scratchpad, never in the repo. Then open them with the Read tool to look.
- Actions: `wait:S`, `key:NAME`, `hold:K+K:S`, `aim:WX,WY`, `pixel:X,Y`, `click[:COUNT[:GAP]]`, `shot:NAME`. Run `python .claude/skills/run-game/drive.py --help` for details.
- The game is closed with WM_CLOSE, so `Unload` runs, and its whole console output is printed at the end.

## The shooter

| Input | Effect |
| --- | --- |
| `W A S D` / arrows | walk (5 units/s) |
| mouse | aim; the laser stops at the first enemy, wall or crate |
| left click | fire one tracer |
| `K` | remove every enemy |
| `F2` | draw collider outlines (green solid, yellow trigger, blue static) |
| `F3` | log sprites, draw calls, entities, colliders and shape tests of the last frame |
| `Esc` | quit |

- The camera follows the player, so the player is always at the window's center, and `aim:WX,WY` aims WX, WY world units away from it (90 pixels per unit at the shooter's zoom).
- The arena is 30×20 units around the origin and the player starts at (0, 0). The crate is at (3, 2) (1.2 units wide). The first enemy starts at (-5, 3) and walks in at 2 units/s, so `aim:-2.5,1.5` points along its path for the first 2 seconds. More enemies appear along the walls every 1.5 s.
- Walking from the center: about 3 s to reach a side wall (15 units at 5 units/s).

## What to check

- The log has no `[ERROR]` lines and no exception, and the build has no warnings.
- Use F3 for numbers instead of guessing: press it during the situation you want to measure (bullets in the air, many enemies), and quote what it printed.
- Look at the screenshots yourself (Read the PNG) before saying something looks right: sprites lined up with F2's outlines, things stopping where they should.
- Say what you couldn't check. A screenshot can't show a one-frame stutter or smoothness; tell the user to look for it when they play.
- Rebuild before running after an edit (`--build`), or you'll test the old build.
