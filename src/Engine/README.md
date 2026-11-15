# Engine

The engine itself: a class library built from scratch on Silk.NET and OpenGL 3.3 core. Apps such as [`src/Sandbox`](../Sandbox) reference it, derive from `Game` and call `Run()`. See [ROADMAP.md](../../ROADMAP.md) for where it's going.

Everything is in the `Engine` namespace.

| File | What it is |
| --- | --- |
| [`Game.cs`](Game.cs) | Base class for a game: window, GL context, main loop with a fixed step |
| [`Input/Input.cs`](Input/Input.cs) | Keyboard and mouse state: held, pressed and released this frame, mouse position |
| [`Log.cs`](Log.cs) | Console logging with three levels (info, warning, error) that can be filtered or turned off |
| [`Graphics/Camera2D.cs`](Graphics/Camera2D.cs) | Orthographic 2D camera: position, view height, matrices, screen-to-world |
| [`Graphics/QuadRenderer.cs`](Graphics/QuadRenderer.cs) | Draws flat-colored quads from one unit square, with a model matrix per draw |
| [`Graphics/ShaderProgram.cs`](Graphics/ShaderProgram.cs) | Compiles and links GLSL, throwing with the driver's log on errors |
| [`Rectangle.cs`](Rectangle.cs) | Axis-aligned rectangle (center, size, color) for UI and previews |
| [`Physics/Body.cs`](Physics/Body.cs) | A rigid box: position, angle, velocities, mass and inertia |
| [`Physics/Physics.cs`](Physics/Physics.cs) | The rigid-body simulation: fixed time step and impulse solver |
| [`Physics/Collision.cs`](Physics/Collision.cs) | Box-box contacts with the separating axis theorem (internal) |

Silk.NET packages (`Input`, `OpenGL`, `Windowing`, all 2.23.0) are referenced here, and projects that reference the engine get them too.

## Making a game

```csharp
using Engine;
using Silk.NET.Input; // for Key and MouseButton

new MyGame().Run();

sealed class MyGame() : Game("My game")
{
    private readonly Camera2D _camera = new(viewHeight: 10f);

    protected override void Resized(System.Numerics.Vector2 size) => _camera.AspectRatio = AspectRatio;

    protected override void FixedUpdate(float step) { /* simulation, 60 times per second */ }

    protected override void Update(float deltaTime)
    {
        if (Input.WasPressed(Key.Escape))
            Exit();
    }

    protected override void Draw()
    {
        Quads.Begin();
        Quads.SetViewProjection(_camera.ViewProjection);
        Quads.Draw(new Rectangle(Center: new(0f, 0f), Size: new(1f, 1f), Color: new(1f, 0.5f, 0.2f)));
    }
}
```

`Load` and `Unload` are there for games that create their own GL resources. `GL`, `Input` and `Quads` exist from `Load` on.

### Logging

`Log.Info`, `Log.Warning` and `Log.Error` write to the console as `[INFO ]`, `[WARN ]` (yellow) and `[ERROR]` (red, on the error stream). `Log.Level` hides everything below it, and `LogLevel.None` turns logging off. Set it before `Run()`:

```csharp
Log.Level = LogLevel.Warning; // only warnings and errors
Log.Level = LogLevel.None;    // silent
new MyGame().Run();
```

The default is `LogLevel.Info`, so everything shows. An interpolated message (`$"..."`) is built even when its level is hidden, so for something logged every frame, check `Log.IsEnabled(level)` first.

## How it works

1. **The game loop (`Game`).** The constructor creates a 1280×720 window with an OpenGL 3.3 core context and hooks Silk.NET's window events. `Run` opens it and loops until `Exit`.
   - **Load.** The GL context only exists once the window is running, so `Game` creates `GL`, `Input` and `Quads` in Silk.NET's `Load` event, logs the OpenGL version and GPU, then calls the game's `Load` and `Resized`.
   - **Each frame:** Silk.NET processes events, `Input.BeginFrame()` takes this frame's snapshot, `FixedUpdate` runs zero or more times, `Update` runs once, then the screen is cleared to `ClearColor` and `Draw` runs once.
   - **Fixed step.** Frame time goes into an accumulator, and `FixedUpdate` runs once for every whole `FixedTimeStep` (1/60 s by default) in it. The same simulation then gives the same result on a 60 Hz and a 240 Hz screen. After a long hitch, at most 8 steps run and the rest is dropped. `FixedStepAlpha` (0..1) says how far into the next step the clock is, so `Draw` can blend between the last two fixed-step positions if motion looks choppy.
   - **Which update?** `FixedUpdate` for simulation that must not depend on frame rate. `Update` for things that should follow the screen smoothly, like the camera and the UI, and for one-off input: a fixed step can run 0 or 2 times in a frame, so a single-frame `WasPressed` could be missed or seen twice there. Held keys (`IsDown`) are fine in both.
   - **Resize.** On `FramebufferResize`, `Game` sets the GL viewport and calls `Resized`, except when minimized (height 0). Games update their camera's `AspectRatio` there.
   - **Screen space.** `ScreenProjection` maps window units straight to the screen, with (0, 0) at the top left and y down, the same space as mouse positions. UI drawn with it doesn't move with the camera.
   - **Closing.** `Unload` runs first, then `Game` disposes `Quads`, the input context and `GL`.
2. **Input (`Input`).** Silk.NET reports key and button changes as events. `Input` records them so game code can ask questions instead:
   - `IsDown(key)` — held right now. `WasPressed(key)` / `WasReleased(key)` — went down / up this frame. The same three exist for `MouseButton`.
   - Events fill "since last frame" flags. `BeginFrame` copies them to "this frame" flags and clears them, so every question gives the same answer for the whole frame, no matter when it's asked.
   - A tap that goes down and up between two frames reports `WasPressed`, `WasReleased` and `IsDown` all true for one frame, so it isn't lost.
   - The flags are plain `bool` arrays indexed by the enum value, so nothing allocates while the game runs. Out-of-range values like `Key.Unknown` (-1) are ignored.
   - `MousePosition` is read once per frame, in window units. `MouseWorldPosition(camera)` turns it into world units with `Camera2D.ScreenToWorld`. `Cursor` sets the cursor shape and ignores repeated sets, so it's fine to assign every frame.
3. **One unit quad (`QuadRenderer`).** The GPU only ever holds a single square: 4 corners from -0.5 to 0.5, each just an (x, y) position.
   - **VBO, EBO and VAO.** The vertex buffer object (VBO) holds the 4 corners. The element buffer object (EBO) holds 6 indices, `0 1 2` and `2 3 0`, that build two triangles from those corners, so the shared corners aren't stored twice. The vertex array object (VAO) records the layout (attribute 0 is a 2-float position) and which EBO is bound. That's why the EBO is bound while the VAO is bound.
   - **Shaders.** The vertex shader computes `uViewProjection * uModel * position`. The fragment shader outputs a flat `uColor`. `ShaderProgram.Create` throws with the driver's error log if compiling or linking fails.
   - **Drawing.** `Begin` binds the shader and the VAO. `SetViewProjection` sets the matrix for a pass: a camera's for the scene, a pixel projection for UI. Each `Draw(model, color)` sets `uColor` and `uModel` and calls `DrawElements(Triangles, 6)`. That's one draw call per quad, fine for a handful of them; the sprite batcher in roadmap step 3 is what scales.
   - **Cleanup.** `QuadRenderer` is `IDisposable` and deletes its buffers, VAO and program. `Game` creates one as `Quads` and disposes it before the GL context.
4. **Rectangles and bodies.** A `Rectangle` is a plain axis-aligned `Center`, `Size` and `Color`, used for things that never rotate. A `Body` is a box with a `Position`, `Size`, `Angle`, `Velocity`, `AngularVelocity` and `Color`. Both have a `Model` matrix that scales the unit quad to `Size`, rotates it (bodies only), then moves it into place.
   - **Mass.** A falling body gets its mass from its area, and a moment of inertia (resistance to spinning) of `m (w² + h²) / 12`, the formula for a solid rectangle. The solver stores the inverses, so a fixed body is just one with inverse mass 0, that is, infinitely heavy.
5. **Camera (`Camera2D`).** It has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio (`AspectRatio`, which the game updates in `Resized`), shapes keep their proportions.
6. **Physics (`Physics.Update`).** A small rigid-body engine modeled on [Box2D Lite](https://github.com/erincatto/box2d-lite), Erin Catto's teaching version of Box2D. The app calls it with its list of bodies and the frame time; it keeps its own, finer fixed step.
   - **Fixed time step.** Frame time is collected in an accumulator and simulated in steps of exactly 1/240 s, so results don't depend on frame rate. Small steps also keep a fast fall to a few hundredths of a unit per step, so a box can't skip through a thin platform. After a long hitch, at most 16 steps run and the rest of the time is dropped instead of freezing the app trying to catch up.
   - **Each step** has four phases:
     1. **Gravity.** Every falling body gains downward velocity (9.81 units/s², so one world unit is one meter).
     2. **Contacts.** `Collision.BoxBox` checks every pair with at least one falling body (see below).
     3. **Solver.** 10 passes of sequential impulses over the contacts (see below).
     4. **Integration.** Bodies move and turn with their corrected velocities. This is semi-implicit Euler: velocity first, then position.
   - **Cleanup.** Falling bodies below y = -50 are removed.
7. **Collision (`Collision.cs`).** Rotated boxes are tested with the **separating axis theorem**. Two boxes are apart if, along any of their 4 face directions, their shadows don't overlap. If every direction overlaps, the one with the least overlap is the contact normal.
   - **Reference and incident faces.** The face of the box that owns that direction becomes the reference face. The face of the other box that points most directly back at it is the incident face.
   - **Clipping.** The incident face is cut to the width of the reference face. Each remaining end that is behind the reference face becomes a contact, with a point, a normal and a depth.
   - **Why it matters.** A box lying flat gets 2 contacts, one under each corner, so it rests without rocking. A box hanging over an edge gets its only support off to one side of its center, so gravity turns it and it tips.
8. **Solver (`Physics.cs`).** Each contact applies an **impulse**, an instant change in momentum, at its point. Because the point is off the body's center, the push changes both the velocity and the spin (`AngularVelocity += cross(offset, impulse) / inertia`).
   - **Normal impulse.** Just enough that the two contact points stop approaching. It can push but never pull, so the running total is clamped at 0 or more.
   - **Friction impulse.** Opposes sliding along the surface, capped at `Friction × normal impulse` (Coulomb friction, μ = 0.5). Grip is what makes a box tip over an edge instead of sliding off it.
   - **Overlap correction (Baumgarte).** Overlap deeper than 0.005 units adds a little extra separating speed, removing 20% of it per step, capped at 2 units/s so a box placed inside another is eased out instead of launched.
   - **Warm starting.** A resting contact needs almost the same push every step. Contacts are matched with last step's (same two bodies, point within 0.02 units) and start from last step's totals. Without this, stacked boxes slowly creep and rotate.
   - **Effective mass.** For each contact, `NormalMass` and `TangentMass` are precomputed: how much relative speed a unit push removes at that point, inverted. This includes both bodies' mass and their resistance to spinning around that point.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `Camera2D.ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`Scale * Translation`, `View * Projection`) while the shader reads right to left (`uViewProjection * uModel * v`).

> **Name clash.** `Silk.NET.Maths` also has a `Rectangle`. A file that imports both namespaces needs `using Rectangle = Engine.Rectangle;`. Games built on `Game` usually only need `Silk.NET.Input` (for `Key` and `MouseButton`), so they rarely hit this.
