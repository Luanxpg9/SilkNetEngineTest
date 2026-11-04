# Engine

The main render engine, built from scratch on Silk.NET and OpenGL 3.3 core, following [LearnOpenGL](https://learnopengl.com/) with the C++ translated to C#.

## Current state: placing rectangles, with rigid-body physics

A scene of rectangles seen through an orthographic 2D camera that pans along X and Y. It starts with a fixed grey platform for the cloth to fall onto and a fixed orange box resting on it; the starting scene is the `bodies` list at the top of `Program.cs`.

A toolbar on the left edge lets you place more rectangles:

1. Click the **Rectangle** button (the orange icon). It turns blue to show it's selected, and the cursor becomes a crosshair over the scene.
2. A **gravity** toggle (a down arrow) appears below it. It starts on (green): placed rectangles fall. Click it, or press `G`, to switch it off (grey) and place fixed rectangles instead, such as more platforms.
3. Click and drag in the scene. A blue preview shows the rectangle as you drag, in any direction.
4. Release to place it, and its position and size are printed to the console.
   - **Falling rectangles** take the next color from a small palette. They're rigid bodies: they fall, rotate, tip over edges, topple off corners, stack, and are removed if they fall off the world.
   - **Fixed rectangles** are grey like the platform and stay where they're placed.

Right click during a drag cancels it. Click the selected button again to deselect the tool. Drags smaller than 0.01 world units are ignored as accidental clicks.

```bash
dotnet run --project src/Engine
```

| Key | Action |
| --- | --- |
| `W` / `↑` | Move camera up (+Y) |
| `S` / `↓` | Move camera down (-Y) |
| `A` / `←` | Move camera left (-X) |
| `D` / `→` | Move camera right (+X) |
| Left click on a toolbar button | Select / deselect that tool |
| Left drag in the scene | Place a rectangle (Rectangle tool selected) |
| Left click on the gravity toggle, or `G` | Switch between placing falling and fixed rectangles |
| Right click | Cancel the current drag |
| `Esc` | Quit |

On startup it prints the OpenGL version and GPU in use.

## How it works

The window, GL setup and frame loop are in [`Program.cs`](Program.cs). The camera is in [`Camera2D.cs`](Camera2D.cs), scene objects are [`Body.cs`](Body.cs), physics is in [`Physics.cs`](Physics.cs) and [`Collision.cs`](Collision.cs), the toolbar layout is in [`Toolbar.cs`](Toolbar.cs), and the axis-aligned [`Rectangle.cs`](Rectangle.cs) is used for the UI and the drag preview.

1. **Window and context.** Silk.NET creates a 1280×720 window with an OpenGL 3.3 core profile context. GL resources are created in `window.Load`, because the context only exists once the window is running.
2. **One unit quad.** The GPU only ever holds a single square: 4 corners from -0.5 to 0.5, each just an (x, y) position.
3. **VBO, EBO and VAO.** The vertex buffer object (VBO) holds the 4 corners. The element buffer object (EBO) holds 6 indices, `0 1 2` and `2 3 0`, that build two triangles from those corners, so the shared corners aren't stored twice. The vertex array object (VAO) records the layout (attribute 0 is a 2-float position) and which EBO is bound. That's why the EBO is bound while the VAO is bound.
4. **Bodies and rectangles.** Everything in the scene is a `Body`: a box with a `Position`, `Size`, `Angle`, `Velocity`, `AngularVelocity` and `Color`. Its `Model` matrix scales the unit quad to `Size`, rotates it by `Angle`, then moves it to `Position`.
   - **Mass.** A falling body gets its mass from its area, and a moment of inertia (resistance to spinning) of `m (w² + h²) / 12`, the formula for a solid rectangle. The solver stores the inverses, so a fixed body is just one with inverse mass 0, that is, infinitely heavy.
   - **`Rectangle`** is a plain axis-aligned `Center`, `Size` and `Color`, used for things that never rotate: the toolbar and the drag preview.
5. **Camera.** `Camera2D` has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically; 2 here). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio, shapes keep their proportions when the window is resized.
6. **Movement.** `window.Update` reads WASD or the arrow keys each tick and moves the camera at a constant 1.5 units per second. The speed is scaled by delta time, so it doesn't depend on frame rate, and the direction is normalized, so diagonals aren't faster.
7. **Shaders.** The vertex shader computes `uViewProjection * uModel * position`. The fragment shader outputs a flat `uColor`. The `CompileShader` and `CreateShaderProgram` helpers throw with the driver's error log if compiling or linking fails.
8. **Drawing.** Each frame: clear, bind the shader program and the VAO, then draw in two passes with the same quad and shader. `DrawQuad` sets `uColor` and `uModel` and calls `DrawElements(Triangles, 6)`, so it's one draw call per box, which is fine for a handful of them.
   - **Scene pass:** `uViewProjection` is the camera's matrix. Draw every body, then the drag preview if a drag is in progress.
   - **UI pass:** `uViewProjection` becomes `CreateOrthographicOffCenter(0, width, height, 0)`, which maps window pixels straight to the screen with (0, 0) at the top left. The toolbar panel, buttons and icons are drawn in pixels, so the camera doesn't move them, and last, so they sit on top of the scene.
9. **Toolbar and mouse.** `Toolbar` is pure layout in window pixels: the panel, one button per tool, hit testing (`ToolAt`) and icons built from small rectangles, so there's no texture or text rendering yet. The mouse handlers in `Program.cs` do the rest:
   - A left click on the panel selects or deselects a tool, or flips the gravity option while the Rectangle tool is selected. Panel clicks never reach the scene.
   - A left click in the scene with the Rectangle tool stores the drag's start in **world** units, using `Camera2D.ScreenToWorld`. Each frame, the preview spans from that start to the mouse's current world position, so panning the camera mid-drag keeps the start pinned to the world.
   - Releasing measures the dragged area with `Rectangle.FromCorners`, which accepts the two corners in any order, and adds a `Body` there that falls or not, depending on the gravity option.
10. **Physics.** `Physics.Update` runs at the start of every `window.Update`, before input. It's a small rigid-body engine modeled on [Box2D Lite](https://github.com/erincatto/box2d-lite), Erin Catto's teaching version of Box2D.
    - **Fixed time step.** Frame time is collected in an accumulator and simulated in steps of exactly 1/240 s, so results don't depend on frame rate. Small steps also keep a fast fall to a few hundredths of a unit per step, so a box can't skip through the 0.15-unit-thick platform. After a long hitch, at most 16 steps run and the rest of the time is dropped instead of freezing the app trying to catch up.
    - **Each step** has four phases:
      1. **Gravity.** Every falling body gains downward velocity (9.81 units/s², so one world unit is one meter).
      2. **Contacts.** `Collision.BoxBox` checks every pair with at least one falling body (see below).
      3. **Solver.** 10 passes of sequential impulses over the contacts (see below).
      4. **Integration.** Bodies move and turn with their corrected velocities. This is semi-implicit Euler: velocity first, then position.
    - **Cleanup.** Falling bodies below y = -50 are removed.
11. **Collision (`Collision.cs`).** Rotated boxes are tested with the **separating axis theorem**. Two boxes are apart if, along any of their 4 face directions, their shadows don't overlap. If every direction overlaps, the one with the least overlap is the contact normal.
    - **Reference and incident faces.** The face of the box that owns that direction becomes the reference face. The face of the other box that points most directly back at it is the incident face.
    - **Clipping.** The incident face is cut to the width of the reference face. Each remaining end that is behind the reference face becomes a contact, with a point, a normal and a depth.
    - **Why it matters.** A box lying flat gets 2 contacts, one under each corner, so it rests without rocking. A box hanging over an edge gets its only support off to one side of its center, so gravity turns it and it tips.
12. **Solver (`Physics.cs`).** Each contact applies an **impulse**, an instant change in momentum, at its point. Because the point is off the body's center, the push changes both the velocity and the spin (`AngularVelocity += cross(offset, impulse) / inertia`).
    - **Normal impulse.** Just enough that the two contact points stop approaching. It can push but never pull, so the running total is clamped at 0 or more.
    - **Friction impulse.** Opposes sliding along the surface, capped at `Friction × normal impulse` (Coulomb friction, μ = 0.5). Grip is what makes a box tip over an edge instead of sliding off it.
    - **Overlap correction (Baumgarte).** Overlap deeper than 0.005 units adds a little extra separating speed, removing 20% of it per step, capped at 2 units/s so a box placed inside another is eased out instead of launched.
    - **Warm starting.** A resting contact needs almost the same push every step. Contacts are matched with last step's (same two bodies, point within 0.02 units) and start from last step's totals. Without this, stacked boxes slowly creep and rotate.
    - **Effective mass.** For each contact, `NormalMass` and `TangentMass` are precomputed: how much relative speed a unit push removes at that point, inverted. This includes both bodies' mass and their resistance to spinning around that point.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`Scale * Translation`, `View * Projection`) while the shader reads right to left (`uViewProjection * uModel * v`).

## Next steps

Toward the first goal, a 2D cloth mesh:

- A grid of cloth points above the platform, drawn as lines with an EBO
- Cloth simulation (Verlet integration and distance constraints), reusing the fixed time step and colliding with the bodies
- Physics extras: bounciness (restitution), per-body friction and density, and sleeping for bodies at rest
- Faster collision checks for big scenes (a broad phase, like `src/Circles`' grid; right now every pair is tested)
- Zooming the camera (changing `ViewHeight`)
- More tools: select, move and delete rectangles, and a Cloth tool for placing the cloth
