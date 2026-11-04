# Engine

The main render engine, built from scratch on Silk.NET and OpenGL 3.3 core, following [LearnOpenGL](https://learnopengl.com/) with the C++ translated to C#.

## Current state: triangle and a 2D camera

A single triangle with a red, green and blue corner, seen through an orthographic 2D camera that pans along X and Y. The GPU interpolates the colors across its surface.

```bash
dotnet run --project src/Engine
```

| Key | Action |
| --- | --- |
| `W` / `↑` | Move camera up (+Y) |
| `S` / `↓` | Move camera down (-Y) |
| `A` / `←` | Move camera left (-X) |
| `D` / `→` | Move camera right (+X) |
| `Esc` | Quit |

On startup it prints the OpenGL version and GPU in use.

## How it works

The window, GL setup and frame loop are in [`Program.cs`](Program.cs). The camera is in [`Camera2D.cs`](Camera2D.cs).

1. **Window and context.** Silk.NET creates a 1280×720 window with an OpenGL 3.3 core profile context. GL resources are created in `window.Load`, because the context only exists once the window is running.
2. **Vertex data.** Each vertex is 6 floats: position (x, y, z) and color (r, g, b). Positions are in world units.
3. **VBO and VAO.** The vertex buffer object (VBO) holds the data in GPU memory. The vertex array object (VAO) records how to read it: attribute 0 is position, attribute 1 is color, with a 24-byte stride.
4. **Camera.** `Camera2D` has a `Position` (the world point at the screen's center) and a `ViewHeight` (how many world units fit vertically; 2 here). It builds two matrices:
   - **View** translates the world by `-Position`, so moving the camera right slides everything left.
   - **Projection** is orthographic. It maps a `ViewHeight × aspect` rectangle onto the -1..1 range. Because the width follows the window's aspect ratio, shapes keep their proportions when the window is resized.
5. **Movement.** `window.Update` reads WASD or the arrow keys each tick and moves the camera at a constant 1.5 units per second. The speed is scaled by delta time, so it doesn't depend on frame rate, and the direction is normalized, so diagonals aren't faster.
6. **Shaders.** The vertex shader multiplies each position by `uViewProjection`. The fragment shader outputs the interpolated color. The `CompileShader` and `CreateShaderProgram` helpers throw with the driver's error log if compiling or linking fails.
7. **Drawing.** Each frame: clear, bind the shader program, upload the camera's view-projection matrix, bind the VAO, then call `DrawArrays(Triangles, 0, 3)`.

> **Matrix layout.** `System.Numerics.Matrix4x4` stores matrices row-major and uses row vectors (`v * M`). GLSL reads uniforms column-major and uses `M * v`. Uploading the matrix as-is (transpose = `false`) makes the two conventions line up. This is also why the combined matrix is `View * Projection` in C#.

## Next steps

Toward the first goal, a 2D cloth mesh:

- A grid of vertices drawn with an element buffer (EBO), as lines or triangles
- Zooming the camera (changing `ViewHeight`)
- Cloth simulation (Verlet integration and distance constraints)
