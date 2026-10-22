# HavingFunWithEngine

Learning real-time rendering from scratch in C# with [Silk.NET](https://github.com/dotnet/Silk.NET) and OpenGL 3.3 core. No engine, no framework, just the graphics API.

| Project | What it is |
| --- | --- |
| [`src/Engine`](src/Engine) | The main render engine, built step by step following [LearnOpenGL](https://learnopengl.com/). Currently draws its first triangle. |
| [`src/Circles`](src/Circles) | A side experiment: thousands of circles and sphere impostors drawn with one instanced draw call, with ball-to-ball collisions. |

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A GPU and driver that support OpenGL 3.3

## Running

```bash
dotnet run --project src/Engine
dotnet run --project src/Circles
```

## Roadmap (Engine)

The scope is kept small on purpose, with a clear finish line after step 4.

1. ~~Window, first triangle, shader program~~
2. Textures, camera (view/projection), transforms
3. Model loading (Assimp) and Blinn-Phong lighting
4. Shadow mapping, skybox, a simple scene graph

Maybe later: normal mapping, PBR, deferred shading.
