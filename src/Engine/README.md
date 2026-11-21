# Engine

The engine itself: a class library built from scratch on Silk.NET and OpenGL 3.3 core. Apps such as [`src/Sandbox`](../Sandbox) reference it, derive from `Game` and call `Run()`. See [ROADMAP.md](../../ROADMAP.md) for where it's going.

Everything is in the `Engine` namespace.

| File | What it is |
| --- | --- |
| [`Game.cs`](Game.cs) | Base class for a game: window, GL context, main loop with a fixed step |
| [`Entities/Entity.cs`](Entities/Entity.cs) | Base class for game objects: position with smooth blending between fixed steps, tag, overridable `FixedUpdate` / `Update` / `Draw` |
| [`Entities/Scene.cs`](Entities/Scene.cs) | Owns and runs entities in spawn order; spawn and destroy safely mid-update; find by type or tag |
| [`Input/Input.cs`](Input/Input.cs) | Keyboard and mouse state: held, pressed and released (per frame or per fixed step), mouse position |
| [`MathHelpers.cs`](MathHelpers.cs) | Angle and vector helpers: wrap an angle, direction from an angle, rotate a vector, frame-rate independent smoothing |
| [`Log.cs`](Log.cs) | Console logging with three levels (info, warning, error) that can be filtered or turned off |
| [`Graphics/Camera2D.cs`](Graphics/Camera2D.cs) | Orthographic 2D camera: position, view height, matrices, screen-to-world |
| [`Graphics/SpriteBatch.cs`](Graphics/SpriteBatch.cs) | Draws many textured, tinted, rotated sprites (and flat rectangles and lines) in few draw calls, sorted by layer |
| [`Graphics/Texture.cs`](Graphics/Texture.cs) | An image on the GPU: loads PNGs, premultiplies alpha, nearest or linear filtering (with mipmaps) |
| [`Graphics/TextureRegion.cs`](Graphics/TextureRegion.cs) | A rectangle inside a texture: one frame of a sprite sheet, cut by pixels or as a grid |
| [`Graphics/Animation.cs`](Graphics/Animation.cs) | Frame animation: a list of regions and a frame duration; asks "which frame at time t" |
| [`Graphics/GLDebug.cs`](Graphics/GLDebug.cs) | In Debug builds, sends the driver's OpenGL errors and warnings to `Log` |
| [`Graphics/ShaderProgram.cs`](Graphics/ShaderProgram.cs) | Compiles and links GLSL, throwing with the driver's log on errors |
| [`Rect.cs`](Rect.cs) | Axis-aligned rectangle: center and size only, for layout, hit testing and drawing |
| [`Color.cs`](Color.cs) | RGBA color (0..1 per channel), with ready-made colors and hex codes like `#2B2D35` |

The rigid-body box physics used to live here. It moved to [`src/Sandbox`](../Sandbox) in step 2.5, because games like the shooter don't use it; step 5 brings the engine's own top-down collision.

Silk.NET packages (`Input`, `OpenGL`, `Windowing`, all 2.23.0) and `StbImageSharp` (image loading) are referenced here, and projects that reference the engine get them too.

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
        Sprites.Begin(_camera.ViewProjection);
        Sprites.Draw(new Rect(Center: new(0f, 0f), Size: new(1f, 1f)), Color.FromHex("#FF8033"));
        Sprites.End();
    }
}
```

`Load` and `Unload` are there for games that create their own GL resources. `GL`, `Input` and `Sprites` exist from `Load` on.

### Textures and assets

Put images in an `Assets` folder in the game's project and have the build copy it next to the game:

```xml
<ItemGroup>
  <None Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Then load in `Load`, draw in `Draw`, and dispose in `Unload`:

```csharp
private Texture _crate = null!;

protected override void Load() => _crate = Texture.Load(GL, "Assets/crate.png");
protected override void Unload() => _crate.Dispose();

// in Draw, between Sprites.Begin and Sprites.End:
Sprites.Draw(_crate, position: new(3f, 2f), size: new(1.5f, 1.5f), Color.White);
```

### Sprite sheets, animation and layers

A sprite sheet is one texture holding many frames. `TextureRegion` picks one out, and `Animation` plays a list of them:

```csharp
var sheet = Texture.Load(GL, "Assets/legs_walk.png");
var walk = new Animation(TextureRegion.Grid(sheet, columns: 8, rows: 1), frameDuration: 0.08f);

// each frame, for something that's walking:
_walkTime += deltaTime;
Sprites.Draw(walk.FrameAt(_walkTime), position, size, Color.White, rotation: angle, layer: 20);
```

`TextureRegion.FromPixels(texture, x, y, width, height)` takes a rectangle the way an image editor shows it (y from the top). A whole `Texture` converts to a region by itself, so every `Draw` takes either. An `Animation` holds no clock: each thing playing it keeps its own time, so one animation can be shared.

Every `Draw` takes a `layer` (default 0). Higher layers cover lower ones, whatever order the calls come in, so a game can name its layers (floor, bodies, weapons, effects) and draw things in any order. Within a layer, later calls are on top. `DrawLine(from, to, thickness, color, layer)` draws a flat line, like a laser beam.

`Sprites.DrawCalls` and `Sprites.SpritesDrawn` count this frame's work. Read them in `Update` to get the numbers for the whole previous frame.

### Entities and the scene

A game object derives from `Entity` and overrides what it needs. A `Scene` owns the entities and runs them; the game creates it in `Load` and calls it from its own methods:

```csharp
sealed class Ball(Vector2 position) : Entity(position)
{
    private Vector2 _velocity = new(2f, 0f);

    protected override void FixedUpdate(float step)
    {
        Position += _velocity * step;
        if (Game.Input.WasPressed(Key.Space))
            Destroy();
    }

    protected override void Draw(SpriteBatch sprites) =>
        sprites.Draw(new Rect(DrawPosition, new(0.5f, 0.5f)), Color.White);
}

// in the game:
private Scene _scene = null!;
protected override void Load() { _scene = new Scene(this); _scene.Spawn(new Ball(Vector2.Zero)); }
protected override void FixedUpdate(float step) => _scene.FixedUpdate(step);
protected override void Update(float deltaTime) => _scene.Update(deltaTime);
// in Draw, between Sprites.Begin and Sprites.End:
_scene.Draw(Sprites);
```

Move in `FixedUpdate` and draw at `DrawPosition`, and motion is smooth on any screen (see 10 below). `Teleport(to)` jumps without blending. `Destroy()` (or `scene.Destroy(entity)`) removes an entity; `scene.Spawn(entity)` adds one and returns it, so `var b = scene.Spawn(new Bullet(...))` works. Both are safe in the middle of an update. A destroyed entity stays destroyed (`IsDestroyed`) and can't be spawned again, so a reference to it never comes back to life; spawn a new one instead. `FindFirst<T>()` and `FindAll<T>(list)` find by type, `FindFirstWithTag(tag)` and `FindAllWithTag(tag, list)` by the optional `Tag`. The `FindAll` versions fill a list you pass in, so keep one between frames and finding doesn't allocate. Inside an entity, `Game` is the scene's game (for `Input`), and `Scene` is the scene.

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
   - **Load.** The GL context only exists once the window is running, so `Game` creates `GL` and `Input` in Silk.NET's `Load` event, logs the OpenGL version and GPU, turns on GL error reporting in Debug builds (see 8 below), creates `Sprites`, then calls the game's `Load` and `Resized`.
   - **Each frame:** Silk.NET processes events, `Input.BeginFrame()` takes this frame's snapshot, `FixedUpdate` runs zero or more times, `Update` runs once, then the screen is cleared to `ClearColor` and `Draw` runs once.
   - **Fixed step.** Frame time goes into an accumulator, and `FixedUpdate` runs once for every whole `FixedTimeStep` (1/60 s by default) in it. The same simulation then gives the same result on a 60 Hz and a 240 Hz screen. After a long hitch, at most 8 steps run and the rest is dropped. `FixedStepAlpha` (0..1) says how far into the next step the clock is, so `Draw` can blend between the last two fixed-step positions if motion looks choppy.
   - **Which update?** `FixedUpdate` for simulation that must not depend on frame rate: movement, timers, AI, firing. `Update` for things that should follow the screen smoothly, like the camera and the UI. All of `Input` works in both (see below).
   - **Input around fixed steps.** Before each `FixedUpdate`, `Game` calls `Input.BeginFixedStep()`, and `Input.EndFixedStep()` after it. In between, `WasPressed` and friends answer "since the last fixed step" instead of "since the last frame".
   - **Resize.** On `FramebufferResize`, `Game` sets the GL viewport and calls `Resized`, except when minimized (height 0). Games update their camera's `AspectRatio` there.
   - **Screen space.** `ScreenProjection` maps window units straight to the screen, with (0, 0) at the top left and y down, the same space as mouse positions. UI drawn with it doesn't move with the camera.
   - **Closing.** `Unload` runs first, then `Game` disposes `Sprites`, the input context and `GL`.
2. **Input (`Input`).** Silk.NET reports key and button changes as events. `Input` records them so game code can ask questions instead:
   - `IsDown(key)` — held right now. `WasPressed(key)` / `WasReleased(key)` — went down / up just now. The same three exist for `MouseButton`.
   - **"Just now" depends on where you ask.** In `Update` and `Draw` it means since the previous frame. In `FixedUpdate` it means since the previous fixed step.
   - **Why two answers.** A frame can run 0, 1 or several fixed steps. If fixed steps read the per-frame flags, a click in a frame with 0 steps would be missed, and a click in a frame with 2 steps would fire twice. So presses and releases are tracked twice, in two `Edges` sets: one is latched (moved from "pending" to "current") at each `BeginFrame`, the other at each `BeginFixedStep`. Pending presses wait across frames until a fixed step runs, and the second step in the same frame finds nothing pending. Each press reaches exactly one frame and exactly one fixed step.
   - Between latches, every question gives the same answer, no matter when it's asked.
   - A tap that goes down and up before anyone asks reports `WasPressed`, `WasReleased` and `IsDown` all true for that one frame (or fixed step), so it isn't lost.
   - The flags are plain `bool` arrays indexed by the enum value, so nothing allocates while the game runs. Out-of-range values like `Key.Unknown` (-1) are ignored.
   - `MousePosition` is read once per frame, in window units. `MouseWorldPosition(camera)` turns it into world units with `Camera2D.ScreenToWorld`. `Cursor` sets the cursor shape and ignores repeated sets, so it's fine to assign every frame.
3. **Sprites (`SpriteBatch`).** A sprite is a textured quad with a position, size, rotation, origin and tint. Flat rectangles are sprites too: they use `WhitePixel`, a 1×1 white texture, tinted with the color.
   - **Corners on the CPU.** Each `Draw` computes the sprite's 4 corners itself, the same math as a model matrix (scale, rotate, translate), and stores them with a texture coordinate and the tint. So the shader needs only one matrix, the pass's `uViewProjection`, and there are no per-sprite uniforms that would force a draw call each. The texture coordinates come from the `TextureRegion`, so a sheet frame is just a smaller rectangle of the same texture.
   - **Layers, sorted at the end.** `Draw` doesn't send anything yet: it stores the corners, the texture and a sort key. The key holds the layer in its high 32 bits and the call number in its low 32 bits. At `End`, sorting the keys orders sprites by layer and keeps call order within a layer, and `Array.Sort(keys, indices)` does it without allocating. The arrays double when a pass has more sprites than ever before and are reused after that.
   - **Origin.** The point of the sprite that sits at `position` and that it turns around, from (0, 0) at the bottom left to (1, 1) at the top right. The default (0.5, 0.5) is the center.
   - **One buffer, refilled.** The vertex buffer is sized for 2048 sprites and refilled for every draw call with `BufferSubData`, after "orphaning" it with `BufferData(null)` so the CPU never waits for the GPU to finish drawing the previous contents. The index buffer never changes: `0 1 2 2 3 0` per sprite, offset by 4, built once.
   - **Batching.** After sorting, `End` walks the sprites in layer order and copies their corners into the upload array. When the texture changes, or 2048 sprites are collected, what's there goes out as one upload and one `DrawElements`. So the fewer texture switches in layer order, the fewer draw calls: the shooter's 604 floor and wall sprites share the white pixel in one layer and cost one call; the whole scene takes 9.
   - **Passes.** `Begin(viewProjection)` starts a pass: a camera's matrix for the scene, `ScreenProjection` for the UI. It sets the shader, VAO and blending every time, and turns face culling and depth testing off (culling would hide clockwise quads, which is every quad under `ScreenProjection`), so the batch doesn't depend on state left by other code. `End` unbinds the VAO again: a VAO records which index buffer is bound, so leaving it bound would let any later `BindBuffer(ElementArrayBuffer, ...)` replace the batch's indices. Layers only sort within a pass: a later pass is always on top.
   - **Counting.** `DrawCalls` and `SpritesDrawn` count flushes and sprites since the frame started (`Game` resets them before `Draw`).
   - **Cleanup.** `SpriteBatch` is `IDisposable` and deletes its buffers, VAO, program and `WhitePixel`. `Game` creates one as `Sprites` and disposes it before the GL context.
4. **Shapes and colors are separate (`Rect`, `Color`).** A `Rect` is only where and how big: `Center` and `Size`. Color is passed alongside when drawing: `Sprites.Draw(rect, color)`. That way hit testing, layout, and later collision boxes and sprite regions don't carry a color they don't need.
   - **`Color`** has `R`, `G`, `B` and `A` (alpha, opacity), each from 0 to 1. It comes from floats (`new Color(1f, 0.5f, 0.2f)`), bytes (`Color.FromBytes(255, 128, 51)`) or hex (`Color.FromHex("#FF8033")`, or `#RRGGBBAA` with alpha). A wrong hex code throws right away. `WithAlpha` returns the same color at another opacity. `White`, `Black` and `Transparent` are ready-made.
   - **The name.** It's `Rect`, not `Rectangle`, because `Silk.NET.Maths` already has a `Rectangle` and files importing both would clash.
5. **Camera (`Camera2D`).** It has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio (`AspectRatio`, which the game updates in `Resized`), shapes keep their proportions.

6. **Textures (`Texture`).** `Texture.Load` reads an image with StbImageSharp as RGBA bytes and uploads it with `TexImage2D`. A relative path starts at the folder the game runs from, where the Assets folder is copied, so it works whatever the working directory is.
   - **Flipped on load.** Image files store the top row first, but OpenGL's texture coordinate v = 0 is the bottom row. Flipping makes them agree, so sprites aren't upside down in the world (y up). Note: under `ScreenProjection` (y down) a textured sprite would appear upside down; the UI only uses flat rectangles so far.
   - **Filtering.** `Linear` blends neighboring texels, for smooth art, and builds mipmaps: copies of the image at half size, quarter size and so on, made by `GenerateMipmap`. Art drawn much smaller than its pixel size (the shooter's 400 px per unit art is shown at about 90 px per unit) then samples the copy closest to its screen size instead of skipping pixels, which would shimmer as it moves. `Nearest` keeps hard pixels and has no mipmaps. Edges clamp instead of wrapping, so the opposite edge never bleeds into a sprite's border.
   - **Regions (`TextureRegion`).** A rectangle of a texture in texture coordinates (`UvMin`, `UvMax`). `FromPixels` converts from image-editor pixels (y down from the top) to texture coordinates (v up from the bottom, since the image was flipped). `Grid` cuts a sheet into equal cells in reading order. Filtering and mipmaps average neighboring pixels, so each frame needs transparent space around it inside its cell, or it picks up a sliver of the next frame.
   - **Size limit and cleanup.** The constructor checks the GPU's maximum texture size and throws a clear error for an image that's too big, instead of letting it draw black. `Dispose` deletes the texture only once, because OpenGL reuses texture names and a second delete could hit an unrelated texture.
   - **Premultiplied alpha.** Red, green and blue are multiplied by alpha when the image loads, so a fully transparent pixel really is (0, 0, 0, 0). With plain alpha it would keep a color (often black) that linear filtering mixes into the visible pixels beside it: a dark outline around every sprite. The batcher blends with `One, OneMinusSrcAlpha`, the matching "over" operation, and premultiplies the tint the same way.
7. **Animation (`Animation`).** A list of regions and a frame duration. `FrameIndexAt(time)` is `floor(time / duration)`, wrapped around for looping animations (negative times too, so a cycle can play backwards) or held on the last frame otherwise. It keeps no time of its own, so many characters can share one animation, each at its own point.
8. **GL error reporting (`GLDebug`).** In Debug builds, `Game` asks for a debug context and registers a callback through `KHR_debug` (core in GL 4.3, an extension on most drivers under 3.3). The driver then reports wrong calls the moment they happen: errors as `Log.Error`, performance and other warnings as `Log.Warning`. Messages are synchronous, so a breakpoint in the callback shows the guilty call on the stack. The callback delegate is kept in a static field, because the driver holds a pointer to it and the garbage collector would otherwise free it. Plain notifications and NVIDIA's harmless "shader recompiled" note are skipped. Release builds skip all of it, since the checks cost time.

9. **Math helpers (`MathHelpers`).** `WrapAngle` brings an angle into -π..π, so the difference between two angles turns the short way round. `FromAngle` gives the unit vector `(cos, sin)`, `Rotate` turns a vector, which is handy for offsets defined on art facing +X (a muzzle, a laser) that must follow the sprite. `SmoothingFactor(rate, deltaTime)` is `1 - e^(-rate × deltaTime)`: moving that fraction of the way toward a target each frame follows with the same curve at any frame rate, where `rate × deltaTime` would overshoot on slow frames.

10. **Entities and the scene (`Entity`, `Scene`).** Plain C# objects with virtual methods, owned by a list in the scene; no entity-component system for now.
    - **Smooth motion.** Each entity has `Position` (after the latest fixed step) and `PreviousPosition` (after the one before). `Scene.FixedUpdate` copies one into the other for every entity first, then runs their `FixedUpdate`, so everything that moves an entity in the step, including whatever the game does after the call, is part of one blend. `DrawPosition` lerps between them by `Game.FixedStepAlpha`, read live from the game, so it's right in `Update` and `Draw` alike. Spawning and `Teleport` set both positions, so a new entity doesn't slide in from (0, 0).
    - **Changes during a pass.** `FixedUpdate`, `Update` and `Draw` are passes over the list. Adding to or removing from a list while walking it would skip or repeat entities, so during a pass `Spawn` puts the entity in a queue and `Destroy` only sets `IsDestroyed`. A destroyed entity is skipped at once (not run, drawn or found again, even later in the same pass), and when the pass ends the scene removes destroyed entities (`RemoveAll`, which keeps the order of the rest) and appends the queue. Outside a pass, changes apply right away. Entities spawned mid-pass are already found by the finds, but they only start running in the next pass.
    - **Order.** Entities run in spawn order. Drawing order is decided by sprite layers, not by the list.
    - **Finding.** A straight walk over the list, testing `is T` or the tag. That's plenty for dozens of entities; an index by type can come later if a profile asks for it.
    - **No allocations while running.** The lists only grow when they reach a new largest size, the removal predicate is a `static` lambda (created once), and the finds fill a list the caller keeps.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `Camera2D.ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`View * Projection`) while the shader reads right to left (`uViewProjection * v`).
