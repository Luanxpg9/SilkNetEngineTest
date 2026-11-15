# Sandbox

A physics playground built on [`src/Engine`](../Engine): place falling and fixed rectangles with a toolbar and watch them tip over edges, topple off corners and stack. It's the first app that uses the engine as a library.

```bash
dotnet run --project src/Sandbox
```

## Using it

A scene of rectangles seen through an orthographic 2D camera that pans along X and Y. It starts with a fixed grey platform and a fixed orange box resting on it; the starting scene is the `bodies` list at the top of `Program.cs`.

A toolbar on the left edge lets you place more rectangles:

1. Click the **Rectangle** button (the orange icon). It turns blue to show it's selected, and the cursor becomes a crosshair over the scene.
2. A **gravity** toggle (a down arrow) appears below it. It starts on (green): placed rectangles fall. Click it, or press `G`, to switch it off (grey) and place fixed rectangles instead, such as more platforms.
3. Click and drag in the scene. A blue preview shows the rectangle as you drag, in any direction.
4. Release to place it, and its position and size are printed to the console.
   - **Falling rectangles** take the next color from a small palette. They're rigid bodies: they fall, rotate, tip over edges, topple off corners, stack, and are removed if they fall off the world.
   - **Fixed rectangles** are grey like the platform and stay where they're placed.

Right click during a drag cancels it. Click the selected button again to deselect the tool. Drags smaller than 0.01 world units are ignored as accidental clicks.

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

The window, input and frame loop are in [`Program.cs`](Program.cs), and the toolbar layout is in [`Toolbar.cs`](Toolbar.cs). The camera, quad rendering and physics come from the engine; its [README](../Engine/README.md) explains how they work.

1. **Window and context.** Silk.NET creates a 1280×720 window with an OpenGL 3.3 core profile context. The engine's `QuadRenderer` is created in `window.Load`, because the context only exists once the window is running, and disposed in `window.Closing`.
2. **Update.** Each tick, `Physics.Update` advances the bodies first. Then WASD or the arrow keys move the camera at a constant 1.5 units per second, scaled by delta time so it doesn't depend on frame rate, with the direction normalized so diagonals aren't faster.
3. **Drawing.** Each frame: clear, `quads.Begin()`, then two passes with the same quads:
   - **Scene pass:** the camera's `ViewProjection`. Draw every body, then the drag preview if a drag is in progress.
   - **UI pass:** `CreateOrthographicOffCenter(0, width, height, 0)`, which maps window pixels straight to the screen with (0, 0) at the top left. The toolbar panel, buttons and icons are drawn in pixels, so the camera doesn't move them, and last, so they sit on top of the scene.
4. **Toolbar and mouse.** `Toolbar` is pure layout in window pixels: the panel, one button per tool, hit testing (`ToolAt`) and icons built from small rectangles, so there's no texture or text rendering yet. The mouse handlers in `Program.cs` do the rest:
   - A left click on the panel selects or deselects a tool, or flips the gravity option while the Rectangle tool is selected. Panel clicks never reach the scene.
   - A left click in the scene with the Rectangle tool stores the drag's start in **world** units, using `Camera2D.ScreenToWorld`. Each frame, the preview spans from that start to the mouse's current world position, so panning the camera mid-drag keeps the start pinned to the world.
   - Releasing measures the dragged area with `Rectangle.FromCorners`, which accepts the two corners in any order, and adds a `Body` there that falls or not, depending on the gravity option.

## Ideas for later

- Zooming the camera (changing `ViewHeight`)
- More tools: select, move and delete rectangles
- Physics extras: bounciness (restitution), per-body friction and density, and sleeping for bodies at rest
- A cloth simulation (Verlet integration and distance constraints) that falls onto the bodies
