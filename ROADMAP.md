# Roadmap

**Goal:** turn `src/Engine` into a small 2D engine that can be used to make 2D games. The first game built on it is a **top-down shooter** (`src/TopDownShooter`), and it decides what the engine needs next: every step below should end with something you can run.

The engine stays generic: nothing specific to the shooter goes into `src/Engine`. Game-specific code lives in the game project.

## Projects

| Project | Role |
| --- | --- |
| `src/Engine` | Class library: window and game loop, 2D rendering, input, camera, collision, assets. |
| `src/Sandbox` | The physics playground (toolbar, drag to place boxes, falling rigid bodies), with its own rigid-body physics. First app built on the engine. |
| `src/TopDownShooter` | The game. Created in step 2 phase B and grows with every step. |
| `src/Circles` | Side experiment: instanced circles and sphere impostors. Not part of the roadmap. |

## Steps

### 1. Split the project
- [x] Turn `src/Engine` into a class library (camera, rectangles, quad renderer, shader helpers, rigid-body physics)
- [x] Move the current demo (toolbar, scene, mouse placement) into `src/Sandbox`, behaving exactly as before
- [x] Update the solution, READMEs and this roadmap

### 2. Game loop and input

#### Phase A: engine side
The engine takes over the window and the loop, so apps stop wiring Silk.NET events by hand.
- [x] `Game` base class that owns the window and GL context, with overridable `Load` / `Update` / `Draw` / `Unload`
- [x] Fixed time step for game logic, separate from rendering (`FixedUpdate`)
- [x] Input state: key and mouse button pressed / held / released this frame, mouse position in window and world coordinates
- [x] Move Sandbox onto `Game`, behaving exactly as before

**Done when:** Sandbox runs on `Game` with no Silk.NET window or input code left in its `Program.cs`.

#### Phase B: the shooter starts
The first game on the engine, using only what Phase A provides.
- [x] Create `src/TopDownShooter` and add it to the solution
- [x] A square player that moves with WASD, at the same speed diagonally
- [x] The player turns to face the mouse
- [x] The camera follows the player

**Done when:** you can walk the square around and aim it with the mouse.

### 2.5. Fixes from the engine review
Small fixes from the silk-engine agent's review of v0.2.0, done before step 3 so the batcher builds on them.
- [x] Move the rigid-body physics (`Body`, `Physics`, `Collision`) from Engine to Sandbox: the shooter won't use it, and step 5 brings the engine's own collision
- [x] A "pressed" check that is safe in `FixedUpdate`: a press is seen by exactly one fixed step, even when a frame runs 0 or 2 of them (needed before bullets)
- [x] A `Color` type with red, green, blue and alpha (transparency), ready-made colors like `Color.White`, and creation from hex codes like `#2B2D35`; it replaces the `Vector3` colors everywhere
- [x] `Rectangle` renamed to `Rect` and holding only position and size (no color); this also ends the clash with Silk.NET's `Rectangle`
- [x] Draw calls take shape and color separately: `Draw(rect, color)`
- [x] (Brought forward from step 3 Phase A) The toolbar draws straight into the renderer instead of returning lists of rectangles, so it no longer creates objects every frame

**Decided, no change needed:** a shader that fails to compile throws and stops the game, which is the intended behavior. Sandbox's physics micro-stutter on fast screens and devices plugged in after startup are left for later refinement.

### 3. Sprite rendering

#### Phase A: textures and the sprite batcher
The rendering core: many textured quads per draw call, replacing one draw call per quad.
- [x] GPU error reporting: in Debug builds, ask the driver to report OpenGL errors through a debug callback (`KHR_debug`) and send them to `Log`
- [x] `Texture`: load PNGs (StbImageSharp), flipped for OpenGL, with nearest or linear filtering, `IDisposable`
- [x] Assets folder in each game, copied next to the built game
- [x] Alpha blending, set up so transparent edges don't get dark fringes
- [x] `SpriteBatch`: corners computed on the CPU from position, size, rotation, origin and tint, written into a buffer that is refilled every frame and drawn in one call
- [x] Start a new batch when the texture changes or the buffer is full
- [x] A 1×1 white texture, so flat-colored rectangles are tinted sprites and `SpriteBatch` can replace `QuadRenderer`
- [x] Move the shooter and Sandbox onto `SpriteBatch`, then remove `QuadRenderer` (the toolbar already draws straight into the renderer since step 2.5, so it just switches to the batch)
- [x] Draw call count available for checking (logged or queryable)

**Done when:** both apps look exactly as before, the shooter's arena takes one draw call instead of about 600, and the shooter shows one textured sprite.

#### Phase B: sprite sheets and art
Built on Phase A; mostly bookkeeping on top of the batcher, plus the game's first real art.

**Art direction:** smooth, detailed art, not pixel art. Sprites are drawn at high resolution and scaled down with linear filtering and mipmaps, so fine detail survives at any zoom. All art is made for this project (no assets with unclear licenses), kept as editable source files next to the exported PNGs. Claude drafts the art as SVG files (editable in Inkscape) and a script exports them to PNG.

**The soldier and the rifle:**
- **Rifle:** M16A4, classic black furniture. From above: flat-top upper with a Picatinny rail, red dot sight on the upper rail, rail handguard, laser aiming module on the handguard rail, front sight post, A2 birdcage flash hider, charging handle, fixed buttstock.
- **Hold:** shouldered, aiming straight ahead with both hands.
- **Soldier:** modern kit: helmet, plate carrier, gloves.

- [ ] Sprite sheet regions: draw a sub-rectangle of a texture
- [ ] Frame animation: a sequence of regions with a frame duration
- [ ] Draw order with layers (e.g. floor, bodies, weapon, effects)
- [ ] **The player: a soldier seen from above, holding an M16A4** (see above). The rifle is the star, so it gets real detail
- [ ] The rifle as its own layer on top of the soldier, so it can later recoil and show a muzzle flash
- [ ] **Visible laser beam** from the laser module toward the aim point: a thin, semi-transparent red line with a brighter dot at the end, drawn on the effects layer. It helps the player aim. For now it ends at the mouse; once walls exist (steps 5 and 6) it should stop at the first wall it hits
- [ ] Walk animation for the soldier
- [ ] First-pass sprites for an enemy and a bullet, in the same style

**Done when:** the soldier walks with an animation, aims the M16 at the mouse with the laser beam showing where it points, and the rifle's details are readable in game.

### 4. Entities
- [ ] Plain C# game objects with `Update` / `Draw`, owned by a scene (no ECS for now)
- [ ] Spawn and destroy queues, so objects can be added or removed safely during an update
- [ ] Finding objects by type or tag
- [ ] Shooter: the player fires bullets toward the mouse; bullets disappear after a while

### 5. Top-down collision
- [ ] Circle and axis-aligned box shapes, with circle-circle, box-box and circle-box tests
- [ ] Collision layers and masks (player, enemy, bullet, wall)
- [ ] Solid collisions (push out, slide along walls) vs. triggers (overlap events only)
- [ ] A broad phase (uniform grid, like `src/Circles`) so hundreds of bullets stay cheap
- [ ] Shooter: bullets hit enemies, enemies touch the player, nobody walks through walls

### 6. Tilemap levels
- [ ] Grid of tiles drawn from a tileset texture
- [ ] Solid tiles that block movement, using step 5
- [ ] Load levels from a simple text or JSON file
- [ ] Camera that follows the player and stays inside the level

### 7. Text and HUD
- [ ] Bitmap font rendering through the sprite batcher
- [ ] Screen-space UI pass (like the Sandbox toolbar) for health, ammo and score
- [ ] Simple debug overlay: FPS, entity count, collision shapes

### 8. Audio
- [ ] Load WAV / OGG sound effects and play them through OpenAL (Silk.NET.OpenAL)
- [ ] Volume, looping background music
- [ ] Shooter: shot, hit and death sounds

### 9. The game
- [ ] Enemies that chase the player
- [ ] Health, damage, death
- [ ] Waves that get harder, and a score
- [ ] Title, game over and restart screens

## Later, maybe
- Camera zoom and screen shake
- Particles (muzzle flash, hit sparks)
- Gamepad input
- Cloth simulation and physics extras in Sandbox (restitution, per-body friction, sleeping)
