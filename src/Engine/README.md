# Engine

The main render engine, built from scratch on Silk.NET and OpenGL 3.3 core, following [LearnOpenGL](https://learnopengl.com/) with the C++ translated to C#.

## Current state: placing rectangles with a toolbar

A scene of fixed rectangles seen through an orthographic 2D camera that pans along X and Y. It starts with a grey platform for the cloth to fall onto and an orange box resting on it; the starting scene is the `rectangles` list at the top of `Program.cs`.

A toolbar on the left edge lets you place more rectangles:

1. Click the **Rectangle** button (the orange icon). It turns blue to show it's selected, and the cursor becomes a crosshair over the scene.
2. Click and drag in the scene. A blue preview shows the rectangle as you drag, in any direction.
3. Release to place it. Each placed rectangle takes the next color from a small palette, and its position and size are printed to the console.

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
| Right click | Cancel the current drag |
| `Esc` | Quit |

On startup it prints the OpenGL version and GPU in use.

## How it works

The window, GL setup and frame loop are in [`Program.cs`](Program.cs). The camera is in [`Camera2D.cs`](Camera2D.cs), the rectangle type is in [`Rectangle.cs`](Rectangle.cs), and the toolbar layout is in [`Toolbar.cs`](Toolbar.cs).

1. **Window and context.** Silk.NET creates a 1280×720 window with an OpenGL 3.3 core profile context. GL resources are created in `window.Load`, because the context only exists once the window is running.
2. **One unit quad.** The GPU only ever holds a single square: 4 corners from -0.5 to 0.5, each just an (x, y) position.
3. **VBO, EBO and VAO.** The vertex buffer object (VBO) holds the 4 corners. The element buffer object (EBO) holds 6 indices, `0 1 2` and `2 3 0`, that build two triangles from those corners, so the shared corners aren't stored twice. The vertex array object (VAO) records the layout (attribute 0 is a 2-float position) and which EBO is bound. That's why the EBO is bound while the VAO is bound.
4. **Rectangles.** `Rectangle` is a `Center`, a `Size` and a `Color` in world units. Its `Model` matrix scales the unit quad to `Size` and then moves it to `Center`. `Min` and `Max` give its edges, ready for the cloth's collision checks later. Rectangles are fixed: nothing moves them.
5. **Camera.** `Camera2D` has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically; 2 here). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio, shapes keep their proportions when the window is resized.
6. **Movement.** `window.Update` reads WASD or the arrow keys each tick and moves the camera at a constant 1.5 units per second. The speed is scaled by delta time, so it doesn't depend on frame rate, and the direction is normalized, so diagonals aren't faster.
7. **Shaders.** The vertex shader computes `uViewProjection * uModel * position`. The fragment shader outputs a flat `uColor`. The `CompileShader` and `CreateShaderProgram` helpers throw with the driver's error log if compiling or linking fails.
8. **Drawing.** Each frame: clear, bind the shader program and the VAO, then draw in two passes with the same quad and shader. `DrawRectangle` sets `uColor` and `uModel` and calls `DrawElements(Triangles, 6)`, so it's one draw call per rectangle, which is fine for a handful of them.
   - **Scene pass:** `uViewProjection` is the camera's matrix. Draw every rectangle, then the drag preview if a drag is in progress.
   - **UI pass:** `uViewProjection` becomes `CreateOrthographicOffCenter(0, width, height, 0)`, which maps window pixels straight to the screen with (0, 0) at the top left. The toolbar panel, buttons and icons are drawn in pixels, so the camera doesn't move them, and last, so they sit on top of the scene.
9. **Toolbar and mouse.** `Toolbar` is pure layout in window pixels: the panel, one button per tool, hit testing (`ToolAt`) and icons built from small rectangles, so there's no texture or text rendering yet. The mouse handlers in `Program.cs` do the rest:
   - A left click on the panel selects or deselects a tool and never reaches the scene.
   - A left click in the scene with the Rectangle tool stores the drag's start in **world** units, using `Camera2D.ScreenToWorld`. Each frame, the preview spans from that start to the mouse's current world position, so panning the camera mid-drag keeps the start pinned to the world.
   - Releasing builds the rectangle with `Rectangle.FromCorners`, which accepts the two corners in any order, and adds it to the scene.

> **Screen to world.** Mouse positions arrive in window pixels with y pointing down. `ScreenToWorld` turns them into normalized device coordinates (-1..1, y up), scales by half the visible size and adds the camera position. That's the inverse of the camera's matrices, worked out by hand because an orthographic camera makes it simple.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. That's why the C# code composes left to right (`Scale * Translation`, `View * Projection`) while the shader reads right to left (`uViewProjection * uModel * v`).

## Next steps

Toward the first goal, a 2D cloth mesh:

- A grid of cloth points above the platform, drawn as lines with an EBO
- Cloth simulation (Verlet integration and distance constraints), with gravity and collisions against the rectangles
- Zooming the camera (changing `ViewHeight`)
- More tools: select, move and delete rectangles, and a Cloth tool for placing the cloth
