# Engine

The engine itself: a class library built from scratch on Silk.NET and OpenGL 3.3 core. Apps such as [`src/Sandbox`](../Sandbox) reference it, derive from `Game` and call `Run()`. See [ROADMAP.md](../../ROADMAP.md) for where it's going.

Everything is in the `Engine` namespace.

| File | What it is |
| --- | --- |
| [`Game.cs`](Game.cs) | Base class for a game: window, GL context, main loop with a fixed step |
| [`Input/Input.cs`](Input/Input.cs) | Keyboard and mouse state: held, pressed and released (per frame or per fixed step), mouse position |
| [`Log.cs`](Log.cs) | Console logging with three levels (info, warning, error) that can be filtered or turned off |
| [`Graphics/Camera2D.cs`](Graphics/Camera2D.cs) | Orthographic 2D camera: position, view height, matrices, screen-to-world |
| [`Graphics/QuadRenderer.cs`](Graphics/QuadRenderer.cs) | Draws flat-colored quads from one unit square, with a model matrix per draw |
| [`Graphics/ShaderProgram.cs`](Graphics/ShaderProgram.cs) | Compiles and links GLSL, throwing with the driver's log on errors |
| [`Rect.cs`](Rect.cs) | Axis-aligned rectangle: center and size only, for layout, hit testing and drawing |
| [`Color.cs`](Color.cs) | RGBA color (0..1 per channel), with ready-made colors and hex codes like `#2B2D35` |

The rigid-body box physics used to live here. It moved to [`src/Sandbox`](../Sandbox) in step 2.5, because games like the shooter don't use it; step 5 brings the engine's own top-down collision.

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
        Quads.Draw(new Rect(Center: new(0f, 0f), Size: new(1f, 1f)), Color.FromHex("#FF8033"));
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
   - **Which update?** `FixedUpdate` for simulation that must not depend on frame rate: movement, timers, AI, firing. `Update` for things that should follow the screen smoothly, like the camera and the UI. All of `Input` works in both (see below).
   - **Input around fixed steps.** Before each `FixedUpdate`, `Game` calls `Input.BeginFixedStep()`, and `Input.EndFixedStep()` after it. In between, `WasPressed` and friends answer "since the last fixed step" instead of "since the last frame".
   - **Resize.** On `FramebufferResize`, `Game` sets the GL viewport and calls `Resized`, except when minimized (height 0). Games update their camera's `AspectRatio` there.
   - **Screen space.** `ScreenProjection` maps window units straight to the screen, with (0, 0) at the top left and y down, the same space as mouse positions. UI drawn with it doesn't move with the camera.
   - **Closing.** `Unload` runs first, then `Game` disposes `Quads`, the input context and `GL`.
2. **Input (`Input`).** Silk.NET reports key and button changes as events. `Input` records them so game code can ask questions instead:
   - `IsDown(key)` — held right now. `WasPressed(key)` / `WasReleased(key)` — went down / up just now. The same three exist for `MouseButton`.
   - **"Just now" depends on where you ask.** In `Update` and `Draw` it means since the previous frame. In `FixedUpdate` it means since the previous fixed step.
   - **Why two answers.** A frame can run 0, 1 or several fixed steps. If fixed steps read the per-frame flags, a click in a frame with 0 steps would be missed, and a click in a frame with 2 steps would fire twice. So presses and releases are tracked twice, in two `Edges` sets: one is latched (moved from "pending" to "current") at each `BeginFrame`, the other at each `BeginFixedStep`. Pending presses wait across frames until a fixed step runs, and the second step in the same frame finds nothing pending. Each press reaches exactly one frame and exactly one fixed step.
   - Between latches, every question gives the same answer, no matter when it's asked.
   - A tap that goes down and up before anyone asks reports `WasPressed`, `WasReleased` and `IsDown` all true for that one frame (or fixed step), so it isn't lost.
   - The flags are plain `bool` arrays indexed by the enum value, so nothing allocates while the game runs. Out-of-range values like `Key.Unknown` (-1) are ignored.
   - `MousePosition` is read once per frame, in window units. `MouseWorldPosition(camera)` turns it into world units with `Camera2D.ScreenToWorld`. `Cursor` sets the cursor shape and ignores repeated sets, so it's fine to assign every frame.
3. **One unit quad (`QuadRenderer`).** The GPU only ever holds a single square: 4 corners from -0.5 to 0.5, each just an (x, y) position.
   - **VBO, EBO and VAO.** The vertex buffer object (VBO) holds the 4 corners. The element buffer object (EBO) holds 6 indices, `0 1 2` and `2 3 0`, that build two triangles from those corners, so the shared corners aren't stored twice. The vertex array object (VAO) records the layout (attribute 0 is a 2-float position) and which EBO is bound. That's why the EBO is bound while the VAO is bound.
   - **Shaders.** The vertex shader computes `uViewProjection * uModel * position`. The fragment shader outputs a flat `uColor`, a `vec4` with alpha. Alpha only shows once blending is turned on in step 3 Phase A. `ShaderProgram.Create` throws with the driver's error log if compiling or linking fails.
   - **Drawing.** `Begin` binds the shader and the VAO. `SetViewProjection` sets the matrix for a pass: a camera's for the scene, a pixel projection for UI. Each `Draw(model, color)` sets `uColor` and `uModel` and calls `DrawElements(Triangles, 6)`. That's one draw call per quad, fine for a handful of them; the sprite batcher in roadmap step 3 is what scales.
   - **Cleanup.** `QuadRenderer` is `IDisposable` and deletes its buffers, VAO and program. `Game` creates one as `Quads` and disposes it before the GL context.
4. **Shapes and colors are separate (`Rect`, `Color`).** A `Rect` is only where and how big: `Center` and `Size`. Its `Model` matrix scales the unit quad to `Size` and moves it to `Center`. Color is passed alongside when drawing: `Quads.Draw(rect, color)`. That way hit testing, layout, and later collision boxes and sprite regions don't carry a color they don't need.
   - **`Color`** has `R`, `G`, `B` and `A` (alpha, opacity), each from 0 to 1. It comes from floats (`new Color(1f, 0.5f, 0.2f)`), bytes (`Color.FromBytes(255, 128, 51)`) or hex (`Color.FromHex("#FF8033")`, or `#RRGGBBAA` with alpha). A wrong hex code throws right away. `WithAlpha` returns the same color at another opacity. `White`, `Black` and `Transparent` are ready-made.
   - **The name.** It's `Rect`, not `Rectangle`, because `Silk.NET.Maths` already has a `Rectangle` and files importing both would clash.
5. **Camera (`Camera2D`).** It has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio (`AspectRatio`, which the game updates in `Resized`), shapes keep their proportions.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `Camera2D.ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`Scale * Translation`, `View * Projection`) while the shader reads right to left (`uViewProjection * uModel * v`).
