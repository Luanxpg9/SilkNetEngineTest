# Roadmap

**Goal:** turn `src/Engine` into a small 2D engine that can be used to make 2D games. The first game built on it is a **top-down shooter** (`src/TopDownShooter`), and it decides what the engine needs next: every step below should end with something you can run.

The engine stays generic: nothing specific to the shooter goes into `src/Engine`. Game-specific code lives in the game project. That goes for the kind of game too: what the shooter needs is built so any 2D game can use it, top-down or side view. Top-down conveniences can be added as helpers, but nothing in the engine assumes a top-down game.

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

- [x] Sprite sheet regions: draw a sub-rectangle of a texture
- [x] Frame animation: a sequence of regions with a frame duration
- [x] Draw order with layers (e.g. floor, bodies, weapon, effects)
- [x] **The player: a soldier seen from above, holding an M16A4** (see above). The rifle is the star, so it gets real detail
- [x] The rifle as its own layer on top of the soldier, so it can later recoil and show a muzzle flash
- [x] **Visible laser beam** from the laser module toward the aim point: a thin, semi-transparent red line with a brighter dot at the end, drawn on the effects layer. It helps the player aim. For now it ends at the mouse; step 5 changes it to stop at a maximum range or at the first body it hits
- [x] Walk animation for the soldier
- [x] First-pass sprites for an enemy and a bullet, in the same style

**Done when:** the soldier walks with an animation, aims the M16 at the mouse with the laser beam showing where it points, and the rifle's details are readable in game.

### 4. Entities

#### Phase A: entities and the scene
The engine side: a home for game objects, so games stop keeping their own lists and update order.
- [x] Plain C# game objects with `Update` / `Draw`, owned by a scene (no ECS for now)
- [x] Smooth motion for every entity: the base holds `Position` and `PreviousPosition`, the scene copies one into the other for all entities at the start of each fixed step (as `Player.BeginFixedStep` used to), and spawning or teleporting sets both, so a new bullet doesn't slide in from (0, 0)
- [x] Spawn and destroy queues, so objects can be added or removed safely during an update
- [x] Finding objects by type or tag
- [x] Move the shooter's player and enemy onto entities in a scene, behaving exactly as before
- [x] A test of the scene in the shooter: enemies walk straight at the player at a fixed speed and disappear when they reach them, a spawner adds one along the walls every 1.5 s (up to 8), and `K` removes them all

**Done when:** the player and enemies live in a scene instead of fields on the game, the player looks and plays exactly as before, and the enemy test shows spawning and destroying working mid-update (enemies arrive, vanish on reaching the player, and `K` clears them).

#### Phase B: bullets
The first thing that spawns and dies during play, using only what Phase A provides.
- [x] Shooter: the player fires bullets toward the mouse; bullets disappear after a while. Aim again in `FixedUpdate` from the real `Position` right before firing, so bullets leave along the laser line
- [x] Additive glow in `SpriteBatch`: an option that adds a sprite's light to what's under it instead of covering it, for tracers, muzzle flashes and the laser glow. With premultiplied alpha it's a color whose alpha is lowered (down to 0 for pure light), so it stays in the same batch
- [x] If F3 shows many draw calls once bullets and entities are in: upload all of a pass's sprites once and draw each texture run with `DrawElementsBaseVertex`, instead of one buffer upload per run. Checked, and not needed yet: with about 10 bullets and a few enemies on screen, F3 shows 10 draw calls

**Done when:** clicking fires glowing bullets that leave along the laser line, move smoothly from the first frame, and disappear after a while.

### 5. 2D collision
Collision for any 2D game, not only top-down ones: a platformer, a puzzle game or a side-scrolling shooter should be able to use it as it is. So it knows nothing about gravity, "up" or how the camera looks at the world. It answers two kinds of question: "what touches what, and how" (with a contact normal and depth, so a game can tell a floor from a wall if it cares), and "move this body this far, stopping at solid things". It doesn't apply forces or simulate rigid bodies; that stays in Sandbox, or a later physics step. Top-down conveniences are welcome as helpers on top, as long as the core doesn't depend on them.

#### Phase A: shapes, queries and moving bodies
The engine side, plus the first thing in the shooter that can't be walked through.
- [x] Shapes: circles and axis-aligned boxes, with circle-circle, box-box and circle-box tests that report whether they overlap and, if so, the contact normal and how deep they overlap
- [x] Colliders on entities: a shape with an offset from the entity's `Position`, solid or a trigger, and a layer plus a mask of layers it collides with. Layers are just bits (up to 32); each game names its own (the shooter's player, enemy, bullet and wall live in the shooter, not the engine)
- [x] A collision world that knows every collider in a scene, including static ones that aren't entities (walls)
- [x] Queries, filtered by layer mask: shapes overlapping a point, a circle or a box; and raycasts that return the first shape hit along a ray, up to a maximum distance (which collider, where, how far, and the surface normal there)
- [x] Moving a body: move a collider by a displacement, stop at solid colliders, and slide along them with the rest of the move. It returns every contact with its normal, so a platformer can check for ground under its feet and a top-down game can ignore which way walls face. It works with any velocity, with or without gravity
- [x] Triggers: overlaps that don't block, reported as begin and end events each fixed step
- [x] A debug view: a key that draws every collider's outline (solid, trigger and static in different colors), on top of the game
- [x] Shooter: the walls and the crate are solid, the player is a circle that slides along them, and the clamp in `Player` goes away
- [x] (Brought forward from Phase B, to try out triggers) Shooter: an enemy's collider is a trigger that notices the player, and reaching the player is its begin event instead of the distance check in `Enemy`

**Done when:** the soldier can't walk through the walls or the crate and slides smoothly along them and around the crate's corners, and the debug view shows every collider lined up with its sprite.

#### Phase B: hits, the laser and many bullets
The shooter puts the collision world to work, and the engine gets fast enough for it.
- [ ] A broad phase (a uniform grid, like `src/Circles`), so queries and moves only test nearby colliders and hundreds of bullets stay cheap. Shown with F3: the number of shape tests per frame stays low with many bullets in the air
- [ ] Shooter: bullets hit enemies and disappear (enemies die in one hit for now), and bullets stop at walls and the crate. Fast bullets don't skip through thin things: a bullet raycasts along its move each step instead of only testing where it ends up
- [ ] Shooter: enemies collide with walls, the crate and each other, and still notice reaching the player. An entity has one collider today and the enemy's is a trigger (from Phase A), so either entities get room for a second collider (a solid body plus a trigger), or the enemy's solid move reports touching the player through its contacts
- [ ] Shooter: the laser no longer ends at the mouse. It's a raycast from the laser module along the rifle, and the beam and its dot stop at the first body it hits (enemy, wall, crate) or at the laser's maximum range, whichever comes first
- [ ] Shooter: a laser beam that doesn't shimmer as it turns. Its core is about 1.4 screen pixels wide with hard edges; keep it at least 2 pixels wide, or draw it with a small texture that fades across its width instead of the flat white pixel

**Done when:** shooting an enemy kills it, bullets stop at walls, enemies push around the crate and each other, the laser stops at whatever it points at, and F3 shows the collision cost staying low with a screen full of bullets.

### 6. Tilemap levels
- [ ] Grid of tiles drawn from a tileset texture
- [ ] Solid tiles that block movement, using step 5
- [ ] Raycasts hit solid tiles too, so the laser stops at the level's walls
- [ ] Load levels from a simple text or JSON file
- [ ] Image loading that doesn't depend on a global setting: `Texture` flips the rows itself (in the same pass as premultiplying) instead of turning on StbImageSharp's process-wide flip flag, so tilesets and other images can later load on a background thread
- [ ] Camera that follows the player and stays inside the level

### 7. Text and HUD
- [ ] Bitmap font rendering through the sprite batcher
- [ ] Screen-space UI pass (like the Sandbox toolbar) for health, ammo and score
- [ ] Simple debug overlay: FPS, entity count, and a switch for step 5's collider view

### 8. Audio
- [ ] Load WAV / OGG sound effects and play them through OpenAL (Silk.NET.OpenAL)
- [ ] Volume, looping background music
- [ ] Shooter: shot, hit and death sounds

### 9. The game
- [ ] Enemies that chase the player, with a walk animation. Sprite sheets with many frames are laid out in a grid (the soldier's 8-frame walk as 4×2, 3072×1024, instead of one 6144 px row), so they stay within what integrated GPUs allow (often 8192 px)
- [ ] Health, damage, death
- [ ] Waves that get harder, and a score
- [ ] Title, game over and restart screens

## Later, maybe
- Camera zoom and screen shake
- Particles (muzzle flash, hit sparks)
- Gamepad input
- More collision shapes (rotated boxes, capsules, polygons), and side-view helpers like one-way platforms and slopes, when a game needs them
- Cloth simulation and physics extras in Sandbox (restitution, per-body friction, sleeping)
