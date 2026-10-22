# Engine

The main render engine, built from scratch on Silk.NET and OpenGL 3.3 core, following [LearnOpenGL](https://learnopengl.com/) with the C++ translated to C#.

## Current state: first triangle

A single triangle with a red, green and blue corner. The GPU interpolates the colors across its surface.

```bash
dotnet run --project src/Engine
```

Press `Esc` to quit. On startup it prints the OpenGL version and GPU in use.

## How it works

Everything is in [`Program.cs`](Program.cs).

1. **Window and context.** Silk.NET creates a 1280×720 window with an OpenGL 3.3 core profile context. GL resources are created in `window.Load`, because the context only exists once the window is running.
2. **Vertex data.** Each vertex is 6 floats: position (x, y, z) and color (r, g, b). Positions are in normalized device coordinates, where -1 to 1 spans the window.
3. **VBO and VAO.** The vertex buffer object (VBO) holds the data in GPU memory. The vertex array object (VAO) records how to read it: attribute 0 is position, attribute 1 is color, with a 24-byte stride.
4. **Shaders.** The vertex shader passes position and color through. The fragment shader outputs the interpolated color. The `CompileShader` and `CreateShaderProgram` helpers throw with the driver's error log if compiling or linking fails.
5. **Drawing.** Each frame: clear, bind the shader program and the VAO, then call `DrawArrays(Triangles, 0, 3)`.

## Known limitation

The triangle looks stretched because normalized device coordinates ignore the window's aspect ratio. A projection matrix fixes this in roadmap step 2 (camera).

## Next steps

- An indexed rectangle using an element buffer (EBO)
- Textures
- Camera and transforms
