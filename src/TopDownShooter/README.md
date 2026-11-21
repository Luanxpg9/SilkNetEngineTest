# Top-Down Shooter

The first game built on [`src/Engine`](../Engine). Each step of the [roadmap](../../ROADMAP.md) adds to it, until it's a playable top-down shooter.

```bash
dotnet run --project src/TopDownShooter
```

## Current state: a soldier with an M16A4

A soldier, seen from above, stands in a checkered arena 30×20 units across, with a grey wall around it. The soldier shoulders an M16A4 and aims it at the mouse, and a red laser beam runs from the laser module on the rifle through the cursor, stopping at the first enemy, wall or crate in its way, with a dot where it lands. Walking plays a walk cycle: the legs turn toward the walking direction while the rifle stays on target. Clicking fires a glowing tracer from the muzzle toward the laser dot; it stops at the first wall, crate or enemy it meets, and one hit kills an enemy. A tracer that hits nothing vanishes after 1.2 seconds. A wooden crate stands in the arena. The walls and the crate are solid: the soldier stops at them and slides along them. Enemies, first-pass raiders, appear along the walls every 1.5 seconds (up to 8 at a time) and walk at the player at 2 units per second, sliding around the walls, the crate and each other. For now, an enemy that reaches the player just disappears: there's no damage yet.

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` / arrow keys | Move (5 units per second, the same diagonally) |
| Mouse | Aim: the soldier turns so the laser points at the cursor |
| `K` | Remove every enemy |
| `F2` | Show or hide every collider's outline |
| `F3` | Log how many sprites, draw calls, entities, colliders and shape tests the last frame had |
| Left click | Fire one round (semi-automatic) |
| `Esc` | Quit |

The camera follows the player, so the soldier stays in the middle of the screen and the arena slides underneath. The player can't leave the arena or walk through the crate: they're solid colliders.

## How it works

[`ShooterGame.cs`](ShooterGame.cs) is the game, a `Game` from the engine. [`Player.cs`](Player.cs) is the soldier and [`Enemy.cs`](Enemy.cs) the enemy, [`EnemySpawner.cs`](EnemySpawner.cs) adds enemies, and [`Bullet.cs`](Bullet.cs) is a tracer; all four are engine `Entity` objects in the game's `Scene`, which runs and draws them. The game keeps a handle to the player for the camera and the mouse. [`Art.cs`](Art.cs) loads the textures and names the draw layers. [`Program.cs`](Program.cs) just runs the game (and has a commented-out line for hiding info logs).

1. **Movement in the fixed step.** `FixedUpdate` runs exactly 60 times per second, however fast the screen is. It calls `Scene.FixedUpdate`, which first remembers where every entity starts the step (see 2), then runs each entity's `FixedUpdate`. The player's reads the held keys (`Input.IsDown`) into a direction, normalizes it and moves by `Speed × step` with the engine's `MoveAndSlide` (see 10). The player was spawned first, so the enemies that run after it in the same step see where it really ended up.
2. **Smooth drawing (interpolation).** On a 144 Hz screen, a player that only moves 60 times per second would look jumpy. So every entity keeps both its latest position and the one from the start of the step, and `DrawPosition` blends between them by the engine's `FixedStepAlpha` (how far the clock is into the next step, 0 to 1). The start is saved by the scene before any entity moves, so everything that moves the player during the step (walking, and sliding along walls) is part of the same blend. `Teleport` sets both, for a jump with no blending. The player is drawn slightly in the past, at most one step (1/60 s), but it moves smoothly at any refresh rate.
3. **Camera, aim and animation, per frame.** `Update` runs once per frame. It checks `Esc` there because that's a one-off press. Then it:
   - centers the camera on the player's draw position, so the two always match and the player doesn't shake on screen;
   - converts the mouse to world units with that camera (`Input.MouseWorldPosition`) and hands it to the player as `AimPoint`;
   - calls `Scene.Update`: the player aims at that point, casts its laser and advances its walk animation (below), and each enemy finds the player with `Scene.FindFirst<Player>()` and turns toward it;
   - reads `Scene.Collision.ShapeTests` and resets it, so `F3` can log the frame's collision cost (see 10).
4. **Aiming the laser, not the body.** The laser module sits to the right of the soldier's center, so pointing the soldier's center at the mouse would leave the beam passing beside it. `FaceTowards` turns the soldier a little further, by `asin(offset / distance)`, so the laser's line runs straight through the cursor.
   - **Where it stops.** Each frame, `CastLaser` casts a ray (`Scene.Collision.Raycast`) from the module along the rifle, up to `LaserRange` (25 units, past the arena's far corner), on the enemy and wall layers (`LaserMask`). The beam ends at the hit point, or at full range if nothing is in the way. The ray starts from the drawn position, so the beam stays on the rifle; the colliders are where the last fixed step left them, up to 3 centimeters ahead of how a walking enemy is drawn.
   - **Drawing.** A wide faint glow line and a thin brighter core (`Sprites.DrawLine`), and the dot texture at the end only when the ray hit something. The glow line and the dot are drawn with `glow: 1f`, so they add light to the floor instead of tinting it. The core is 0.024 units wide, about 2.2 pixels at this zoom: at 0.016 (1.4 pixels) its hard edges fell between pixels differently at each angle, and the beam shimmered as the soldier turned.
5. **The soldier is four sprites.** All four are turned by the aim angle, and the layers decide what covers what:
   - **Legs** (layer `Legs`): a frame of the walk cycle, turned toward the walking direction.
   - **Body** (layer `Bodies`): torso, plate carrier and arms.
   - **Rifle** (layer `Weapons`): placed with its butt in the right shoulder. It's its own sprite so it can recoil and show a muzzle flash later.
   - **Top** (layer `BodyTops`): helmet and gloves, over the rifle, so the cheek rests on the stock and the fingers wrap around the grip and the handguard.
   The pieces line up through points written in pixels from the SVG files (soldier center, rifle butt, laser aperture). `Art.Offset` turns them into world-unit offsets, `MathHelpers.Rotate` turns those with the soldier, and `Art.Origin` turns a pivot pixel into a sprite origin (for a whole texture or one frame of a sheet).
6. **Walking.** While the player moves, `Animate` advances a walk time and the legs show `art.Walk.FrameAt(walkTime)`, 8 frames at 0.08 s each. The legs swing smoothly toward the walking direction, using `MathHelpers.SmoothingFactor` so the swing looks the same at any frame rate. Walking backwards (more than 90° away from the aim) turns them the other way round and plays the cycle in reverse, so the legs never point behind the body. Standing still shows frame 0, feet at rest in a bladed stance.
7. **Enemies come and go.** Enemies and bullets spawn and destroy entities all the time, in the middle of the scene's passes:
   - **Walking.** In its `FixedUpdate`, an enemy moves toward the player's real `Position` by `Speed × step`, never past it, with `MoveAndSlide`, and plays the walk cycle while it moves. Its collider is a solid circle of radius 0.45 that notices walls, other enemies and the player, so it slides around the crate and the walls and the enemies push around each other (see 10).
   - **Reaching the player.** The move fills a list of contacts, and a contact with the player's collider means the enemy has reached them. When several enemies crowd the player, pushing out of a neighbor can push one back off the player within the same move, so no contact is reported though they touch; being within touching distance after the move (the two radii plus 0.01 units) counts too. Then the enemy calls `Destroy()`. That's inside the scene's fixed step, so the scene only flags it, skips it from then on, and removes it (and its collider) when the step ends.
   - **Shot.** A bullet that hits an enemy calls its `Kill()`, which destroys it the same way: one hit for now.
   - **Spawning.** `EnemySpawner` is an entity with nothing to draw. Its `FixedUpdate` counts the time, and every 1.5 s it counts the enemies with `Scene.FindAll<Enemy>` and, if there are fewer than 8, spawns one at a random spot just inside the walls, at least 6 units from the player when it can find one. The new enemy waits in the scene's queue and starts running in the next pass, and spawning sets both its positions, so it doesn't slide in.
   - **Removing them all.** `K` is read in the spawner's `FixedUpdate`, where a press reaches exactly one fixed step. It finds every entity tagged `"enemy"` (`Scene.FindAllWithTag`) and destroys each one, again mid-pass. Both lists are kept between steps, so finding doesn't allocate.
   - `F3` also logs `Scene.Count`: the player and the spawner, plus the enemies and bullets in flight, and the number of colliders (the walls, the crate, the player and the enemies; bullets have none).
8. **Firing.** A left click is read with `WasPressed` in the player's `FixedUpdate`, where each click reaches exactly one fixed step: one click, one shot.
   - **Aim again first.** The player's `Angle` was last set in `Update`, from the drawn position. Right before firing, `Fire` aims again from the real `Position`, after this step's movement.
   - **A rifle through a wall.** The rifle reaches about 2 units ahead of the soldier's center, so standing against a wall or an enemy puts the muzzle on the other side of it. So `Fire` first casts a ray from the shoulder to the muzzle; if it crosses a wall, the crate or an enemy, the shot hits that right away (an enemy dies) and no tracer flies.
   - **Through the dot.** The bullet starts at the muzzle (rifle pixel 748, 64) and flies toward where the laser stops, cast again from the real position. The barrel sits a few centimeters right of the laser, so flying straight along `Angle` would pass beside the dot. When the laser stops less than 0.5 units ahead of the muzzle (on something touching the rifle), it flies straight along the rifle instead.
   - **The bullet** ([`Bullet.cs`](Bullet.cs)) is an entity without a collider. It moves half a unit per fixed step (30 units per second), more than an enemy's radius, so testing only where it lands could skip over an enemy. Instead its `Advance` casts a ray along the whole move each step, on the enemy and wall layers (`Bullet.HitMask`). If the ray hits, the bullet moves to the hit point and `Bullet.Strike` kills the enemy it hit at once (walls and the crate just stop it). The bullet itself is only marked spent and destroyed at the start of the next step: a destroyed entity isn't drawn, so destroying it right away would hide it up to half a unit short of what it hit. Kept one step longer, it's drawn sliding into the impact point. Otherwise it destroys itself after 1.2 seconds. It's drawn turned toward where it flies, on the effects layer with `glow: 1f`.
   - **Moving from the first frame.** The bullet is spawned in the middle of the scene's fixed step, so it joins when the step ends and its own `FixedUpdate` first runs in the next one. Spawning sets both its positions to the muzzle, so on its own it would stand still there for up to a whole step (16.7 ms, one or two frames on a fast screen). So `Fire` calls `bullet.Advance(step)` right after spawning it: that moves `Position` half a unit along the path and leaves `PreviousPosition` at the muzzle, and from its first frame the bullet is drawn sliding out of the muzzle at full speed.
   - The muzzle is worked out from the real position, up to one step of walking (about 0.08 units) ahead of where the soldier is drawn. So while walking, a bullet's first frame can sit that far ahead of the drawn muzzle before it outruns the rifle, which is too small to see.
9. **Layers and draw calls.** `Draw` calls things in any order; the layers in `Art.cs` (floor, props, legs, bodies, weapons, body tops, effects) sort them. Sprites that share a texture in a layer batch together, so the 604 floor and wall sprites are one draw call. Each enemy adds 2 sprites, and bullets add one each but share one texture. With a few enemies and bullets in play, `F3` shows 10 draw calls.
10. **Collision.** The shooter names its collision layers in [`CollisionLayers.cs`](CollisionLayers.cs): player, enemy, bullet (unused: bullets raycast instead of carrying a collider) and wall. The engine only sees bits.
    - **The level.** `Load` adds the four walls and the crate to `Scene.Collision` as colliders without an entity, on the wall layer. The walls use the same rectangles they're drawn with; the crate's box is 1.2 units, the crate itself without the shadow drawn around it.
    - **The player** is a solid circle of radius 0.45 (about the shoulders), on the player layer, whose mask notices walls. `MoveAndSlide` stops it at the walls and the crate and keeps the part of each move that runs along them, so walking into a wall at an angle slides along it, and the round collider rolls around the crate's corners.
    - **The enemies** are solid circles of radius 0.45 on the enemy layer, whose mask notices walls, enemies and the player (see 7). The player's mask notices only walls, so enemies never block the player; they only stop themselves against it. That's the one-way nature of masks.
    - **Raycasts.** Bullets, the laser and the shoulder-to-muzzle check in `Fire` are rays on the enemy and wall layers (see 4 and 8). The shooter uses no triggers now: enemies find the player through their move's contacts.
    - **Cost.** The engine files colliders in a grid of 2-unit cells and tests only the ones near each query, move and ray. `F3` logs the shape tests of the last frame: with a few enemies and a dozen bullets in the air it shows about 5 to 14, most of them the laser's 25-unit ray. A bullet over open floor finds empty cells and runs no test at all; without the grid, a dozen bullets alone would run about 100 tests per step.
    - **F2** draws every collider's outline over the game: the player and the enemies in green, the walls and the crate in blue.

## The art

All art is made for this project and kept as editable SVG files next to the exported PNGs in [`Assets/`](Assets). Only the PNGs are copied to the build.

| File | What it is |
| --- | --- |
| `rifle.svg` | The M16A4 from above: A2 stock, charging handle, flat-top upper with a Picatinny rail, red dot sight, quad-rail handguard with a ladder cover, laser aiming module (tan) on the left rail, A2 front sight, A2 birdcage flash hider |
| `soldier_base.svg` / `soldier_top.svg` | The soldier's body and arms / helmet and gloves, on the same canvas |
| `legs_walk.svg` | The walk cycle, 8 frames of 768×512, generated by [`tools/make_walk_sheet.py`](../../tools/make_walk_sheet.py) |
| `enemy.svg` | First-pass enemy: a raider in a jacket and beanie, with a wood-furnished rifle |
| `bullet.svg` | First-pass tracer bullet, drawn as pure light (`glow: 1f`) |
| `laser_dot.svg` | The glow where the laser lands |
| `crate.png` | Placeholder crate from step 3 Phase A, made by [`tools/make_crate.py`](../../tools/make_crate.py) |

Characters are drawn at 400 px per world unit, facing right (+X), and shown at about 90 px per unit, so linear filtering and mipmaps keep the detail smooth. After editing an SVG, export it:

```bash
python tools/export_art.py
```

[`tools/export_art.py`](../../tools/export_art.py) opens each SVG in Edge (or Chrome) in headless mode, at its exact size on a transparent background, and saves a PNG next to it. No extra packages are needed.
