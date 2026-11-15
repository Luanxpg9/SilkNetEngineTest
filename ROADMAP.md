# Roadmap

**Goal:** turn `src/Engine` into a small 2D engine that can be used to make 2D games. The first game built on it is a **top-down shooter** (`src/TopDownShooter`), and it decides what the engine needs next: every step below should end with something you can run.

The engine stays generic: nothing specific to the shooter goes into `src/Engine`. Game-specific code lives in the game project.

## Projects

| Project | Role |
| --- | --- |
| `src/Engine` | Class library: window and game loop, 2D rendering, input, camera, collision and physics, assets. |
| `src/Sandbox` | The physics playground (toolbar, drag to place boxes, falling rigid bodies). First app built on the engine. |
| `src/TopDownShooter` | The game. Created in step 2 and grows with every step. |
| `src/Circles` | Side experiment: instanced circles and sphere impostors. Not part of the roadmap. |

## Steps

### 1. Split the project
- [x] Turn `src/Engine` into a class library (camera, rectangles, quad renderer, shader helpers, rigid-body physics)
- [x] Move the current demo (toolbar, scene, mouse placement) into `src/Sandbox`, behaving exactly as before
- [x] Update the solution, READMEs and this roadmap

### 2. Game loop and input
- [ ] `Game` base class that owns the window and GL context, with overridable `Load` / `Update` / `Draw` / `Unload`
- [ ] Fixed time step for game logic, separate from rendering
- [ ] Input state: key and mouse button pressed / held / released this frame, mouse position in window and world coordinates
- [ ] Move Sandbox onto `Game`
- [ ] Create `src/TopDownShooter`: a square player that moves with WASD and faces the mouse

### 3. Sprite rendering
- [ ] Load textures from PNG (StbImageSharp)
- [ ] Sprite batcher: many textured quads in one draw call, with position, size, rotation, origin and tint
- [ ] Sprite sheets (sub-rectangles of a texture) and frame animation
- [ ] Draw order (layers) and alpha blending
- [ ] Player, enemies and bullets drawn as sprites (placeholder art is fine)

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
