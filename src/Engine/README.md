# Engine

The engine itself: a class library built from scratch on Silk.NET and OpenGL 3.3 core. Apps such as [`src/Sandbox`](../Sandbox) reference it and provide the window and the main loop (until roadmap step 2 moves those in here too). See [ROADMAP.md](../../ROADMAP.md) for where it's going.

Everything is in the `Engine` namespace.

| File | What it is |
| --- | --- |
| [`Graphics/Camera2D.cs`](Graphics/Camera2D.cs) | Orthographic 2D camera: position, view height, matrices, screen-to-world |
| [`Graphics/QuadRenderer.cs`](Graphics/QuadRenderer.cs) | Draws flat-colored quads from one unit square, with a model matrix per draw |
| [`Graphics/ShaderProgram.cs`](Graphics/ShaderProgram.cs) | Compiles and links GLSL, throwing with the driver's log on errors |
| [`Rectangle.cs`](Rectangle.cs) | Axis-aligned rectangle (center, size, color) for UI and previews |
| [`Physics/Body.cs`](Physics/Body.cs) | A rigid box: position, angle, velocities, mass and inertia |
| [`Physics/Physics.cs`](Physics/Physics.cs) | The rigid-body simulation: fixed time step and impulse solver |
| [`Physics/Collision.cs`](Physics/Collision.cs) | Box-box contacts with the separating axis theorem (internal) |

Silk.NET packages (`Input`, `OpenGL`, `Windowing`, all 2.23.0) are referenced here, and projects that reference the engine get them too.

## How it works

1. **One unit quad (`QuadRenderer`).** The GPU only ever holds a single square: 4 corners from -0.5 to 0.5, each just an (x, y) position.
   - **VBO, EBO and VAO.** The vertex buffer object (VBO) holds the 4 corners. The element buffer object (EBO) holds 6 indices, `0 1 2` and `2 3 0`, that build two triangles from those corners, so the shared corners aren't stored twice. The vertex array object (VAO) records the layout (attribute 0 is a 2-float position) and which EBO is bound. That's why the EBO is bound while the VAO is bound.
   - **Shaders.** The vertex shader computes `uViewProjection * uModel * position`. The fragment shader outputs a flat `uColor`. `ShaderProgram.Create` throws with the driver's error log if compiling or linking fails.
   - **Drawing.** `Begin` binds the shader and the VAO. `SetViewProjection` sets the matrix for a pass: a camera's for the scene, a pixel projection for UI. Each `Draw(model, color)` sets `uColor` and `uModel` and calls `DrawElements(Triangles, 6)`. That's one draw call per quad, fine for a handful of them; the sprite batcher in roadmap step 3 is what scales.
   - **Cleanup.** `QuadRenderer` is `IDisposable` and deletes its buffers, VAO and program. The app disposes it before the GL context.
2. **Rectangles and bodies.** A `Rectangle` is a plain axis-aligned `Center`, `Size` and `Color`, used for things that never rotate. A `Body` is a box with a `Position`, `Size`, `Angle`, `Velocity`, `AngularVelocity` and `Color`. Both have a `Model` matrix that scales the unit quad to `Size`, rotates it (bodies only), then moves it into place.
   - **Mass.** A falling body gets its mass from its area, and a moment of inertia (resistance to spinning) of `m (w² + h²) / 12`, the formula for a solid rectangle. The solver stores the inverses, so a fixed body is just one with inverse mass 0, that is, infinitely heavy.
3. **Camera (`Camera2D`).** It has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio (`AspectRatio`, which the app updates on resize), shapes keep their proportions.
4. **Physics (`Physics.Update`).** A small rigid-body engine modeled on [Box2D Lite](https://github.com/erincatto/box2d-lite), Erin Catto's teaching version of Box2D. The app calls it once per frame with its list of bodies.
   - **Fixed time step.** Frame time is collected in an accumulator and simulated in steps of exactly 1/240 s, so results don't depend on frame rate. Small steps also keep a fast fall to a few hundredths of a unit per step, so a box can't skip through a thin platform. After a long hitch, at most 16 steps run and the rest of the time is dropped instead of freezing the app trying to catch up.
   - **Each step** has four phases:
     1. **Gravity.** Every falling body gains downward velocity (9.81 units/s², so one world unit is one meter).
     2. **Contacts.** `Collision.BoxBox` checks every pair with at least one falling body (see below).
     3. **Solver.** 10 passes of sequential impulses over the contacts (see below).
     4. **Integration.** Bodies move and turn with their corrected velocities. This is semi-implicit Euler: velocity first, then position.
   - **Cleanup.** Falling bodies below y = -50 are removed.
5. **Collision (`Collision.cs`).** Rotated boxes are tested with the **separating axis theorem**. Two boxes are apart if, along any of their 4 face directions, their shadows don't overlap. If every direction overlaps, the one with the least overlap is the contact normal.
   - **Reference and incident faces.** The face of the box that owns that direction becomes the reference face. The face of the other box that points most directly back at it is the incident face.
   - **Clipping.** The incident face is cut to the width of the reference face. Each remaining end that is behind the reference face becomes a contact, with a point, a normal and a depth.
   - **Why it matters.** A box lying flat gets 2 contacts, one under each corner, so it rests without rocking. A box hanging over an edge gets its only support off to one side of its center, so gravity turns it and it tips.
6. **Solver (`Physics.cs`).** Each contact applies an **impulse**, an instant change in momentum, at its point. Because the point is off the body's center, the push changes both the velocity and the spin (`AngularVelocity += cross(offset, impulse) / inertia`).
   - **Normal impulse.** Just enough that the two contact points stop approaching. It can push but never pull, so the running total is clamped at 0 or more.
   - **Friction impulse.** Opposes sliding along the surface, capped at `Friction × normal impulse` (Coulomb friction, μ = 0.5). Grip is what makes a box tip over an edge instead of sliding off it.
   - **Overlap correction (Baumgarte).** Overlap deeper than 0.005 units adds a little extra separating speed, removing 20% of it per step, capped at 2 units/s so a box placed inside another is eased out instead of launched.
   - **Warm starting.** A resting contact needs almost the same push every step. Contacts are matched with last step's (same two bodies, point within 0.02 units) and start from last step's totals. Without this, stacked boxes slowly creep and rotate.
   - **Effective mass.** For each contact, `NormalMass` and `TangentMass` are precomputed: how much relative speed a unit push removes at that point, inverted. This includes both bodies' mass and their resistance to spinning around that point.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `Camera2D.ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`Scale * Translation`, `View * Projection`) while the shader reads right to left (`uViewProjection * uModel * v`).

> **Name clash.** `Silk.NET.Maths` also has a `Rectangle`. A file that imports both namespaces needs `using Rectangle = Engine.Rectangle;`, as `src/Sandbox/Program.cs` does.
