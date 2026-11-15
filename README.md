# HavingFunWithEngine

A small 2D game engine built from scratch in C# with [Silk.NET](https://github.com/dotnet/Silk.NET) and OpenGL 3.3 core. No engine, no framework, just the graphics API. The first game made with it will be a top-down shooter.

| Project | What it is |
| --- | --- |
| [`src/Engine`](src/Engine) | The engine, a class library: game loop with a fixed step, input, logging, colors, 2D camera and quad rendering so far. |
| [`src/TopDownShooter`](src/TopDownShooter) | The game being built on the engine. So far: walk around an arena with WASD and aim with the mouse. |
| [`src/Sandbox`](src/Sandbox) | A physics playground built on the engine, with its own rigid-body physics: place falling and fixed boxes with a toolbar and watch them tip, topple and stack. |
| [`src/Circles`](src/Circles) | A side experiment: thousands of circles and sphere impostors drawn with one instanced draw call, with ball-to-ball collisions. |

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A GPU and driver that support OpenGL 3.3

## Running

```bash
dotnet run --project src/TopDownShooter
dotnet run --project src/Sandbox
dotnet run --project src/Circles
```

The engine is a library, so it doesn't run on its own.

## Roadmap

See [ROADMAP.md](ROADMAP.md): the steps from here to a playable top-down shooter, with a checklist for each.
