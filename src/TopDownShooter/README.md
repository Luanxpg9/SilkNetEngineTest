# Top-Down Shooter

The first game built on [`src/Engine`](../Engine). Each step of the [roadmap](../../ROADMAP.md) adds to it, until it's a playable top-down shooter.

```bash
dotnet run --project src/TopDownShooter
```

## Current state: a soldier with an M16A4

A soldier, seen from above, stands in a checkered arena 30×20 units across, with a grey wall around it. The soldier shoulders an M16A4 and aims it at the mouse, and a red laser beam runs from the laser module on the rifle to the cursor, with a dot where it lands. Walking plays a walk cycle: the legs turn toward the walking direction while the rifle stays on target. A wooden crate and a first-pass enemy, a raider who turns to watch the player, stand in the arena.

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` / arrow keys | Move (5 units per second, the same diagonally) |
| Mouse | Aim: the soldier turns so the laser points at the cursor |
| `F3` | Log how many sprites and draw calls the last frame took |
| `Esc` | Quit |

The camera follows the player, so the soldier stays in the middle of the screen and the arena slides underneath. The player can't leave the arena. For now that's a simple clamp; real collision comes in roadmap step 5.

## How it works

[`ShooterGame.cs`](ShooterGame.cs) is the game, a `Game` from the engine. [`Player.cs`](Player.cs) is the soldier and [`Enemy.cs`](Enemy.cs) the enemy. [`Art.cs`](Art.cs) loads the textures and names the draw layers. [`Program.cs`](Program.cs) just runs the game (and has a commented-out line for hiding info logs).

1. **Movement in the fixed step.** `FixedUpdate` runs exactly 60 times per second, however fast the screen is. It first calls `Player.BeginFixedStep`, which remembers where the step starts (see 2). Then it reads the held keys (`Input.IsDown`) into a direction, and `Player.Move` normalizes it and moves the player by `Speed × step`. Then the position is clamped inside the walls, inset by half the player's size.
2. **Smooth drawing (interpolation).** On a 144 Hz screen, a player that only moves 60 times per second would look jumpy. So `Player` keeps both its latest position and the one from the start of the step, and `DrawPosition(alpha)` blends between them. The start is saved in `BeginFixedStep`, not in `Move`, so everything that moves the player during the step (the wall clamp now, collisions later) is part of the same blend. `Teleport` sets both, for a jump with no blending. The engine's `FixedStepAlpha` says how far the clock is into the next step (0 to 1). The player is drawn slightly in the past, at most one step (1/60 s), but it moves smoothly at any refresh rate.
3. **Camera, aim and animation, per frame.** `Update` runs once per frame. It checks `Esc` there because that's a one-off press. Then it:
   - centers the camera on the player's draw position, so the two always match and the player doesn't shake on screen;
   - converts the mouse to world units with that camera (`Input.MouseWorldPosition`) and aims at it (below);
   - advances the walk animation (below), and turns the enemy toward the player.
4. **Aiming the laser, not the body.** The laser module sits to the right of the soldier's center, so pointing the soldier's center at the mouse would leave the beam passing beside it. `FaceTowards` turns the soldier a little further, by `asin(offset / distance)`, so the laser's line runs straight through the cursor. The beam is drawn from the module to the cursor: a wide faint glow line and a thin brighter core (`Sprites.DrawLine`), and the dot texture at the end. With the cursor over the rifle itself, closer than the module, there's no beam.
5. **The soldier is four sprites.** All four are turned by the aim angle, and the layers decide what covers what:
   - **Legs** (layer `Legs`): a frame of the walk cycle, turned toward the walking direction.
   - **Body** (layer `Bodies`): torso, plate carrier and arms.
   - **Rifle** (layer `Weapons`): placed with its butt in the right shoulder. It's its own sprite so it can recoil and show a muzzle flash later.
   - **Top** (layer `BodyTops`): helmet and gloves, over the rifle, so the cheek rests on the stock and the fingers wrap around the grip and the handguard.
   The pieces line up through points written in pixels from the SVG files (soldier center, rifle butt, laser aperture). `Art.Offset` turns them into world-unit offsets, `MathHelpers.Rotate` turns those with the soldier, and `Art.Origin` turns a pivot pixel into a sprite origin (for a whole texture or one frame of a sheet).
6. **Walking.** While the player moves, `Animate` advances a walk time and the legs show `art.Walk.FrameAt(walkTime)`, 8 frames at 0.08 s each. The legs swing smoothly toward the walking direction, using `MathHelpers.SmoothingFactor` so the swing looks the same at any frame rate. Walking backwards (more than 90° away from the aim) turns them the other way round and plays the cycle in reverse, so the legs never point behind the body. Standing still shows frame 0, feet at rest in a bladed stance.
7. **Layers and draw calls.** `Draw` calls things in any order; the layers in `Art.cs` (floor, props, legs, bodies, weapons, body tops, effects) sort them. Sprites that share a texture in a layer batch together, so the 604 floor and wall sprites are one draw call. `F3` shows the whole scene: 614 sprites in 9 draw calls.

## The art

All art is made for this project and kept as editable SVG files next to the exported PNGs in [`Assets/`](Assets). Only the PNGs are copied to the build.

| File | What it is |
| --- | --- |
| `rifle.svg` | The M16A4 from above: A2 stock, charging handle, flat-top upper with a Picatinny rail, red dot sight, quad-rail handguard with a ladder cover, laser aiming module (tan) on the left rail, A2 front sight, A2 birdcage flash hider |
| `soldier_base.svg` / `soldier_top.svg` | The soldier's body and arms / helmet and gloves, on the same canvas |
| `legs_walk.svg` | The walk cycle, 8 frames of 768×512, generated by [`tools/make_walk_sheet.py`](../../tools/make_walk_sheet.py) |
| `enemy.svg` | First-pass enemy: a raider in a jacket and beanie, with a wood-furnished rifle |
| `bullet.svg` | First-pass tracer bullet (used from step 4 on) |
| `laser_dot.svg` | The glow where the laser lands |
| `crate.png` | Placeholder crate from step 3 Phase A, made by [`tools/make_crate.py`](../../tools/make_crate.py) |

Characters are drawn at 400 px per world unit, facing right (+X), and shown at about 90 px per unit, so linear filtering and mipmaps keep the detail smooth. After editing an SVG, export it:

```bash
python tools/export_art.py
```

[`tools/export_art.py`](../../tools/export_art.py) opens each SVG in Edge (or Chrome) in headless mode, at its exact size on a transparent background, and saves a PNG next to it. No extra packages are needed.
