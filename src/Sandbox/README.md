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
4. Release to place it, and its position and size are logged to the console.
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

On startup it logs the OpenGL version and GPU in use. To hide these messages, set `Log.Level` in `Program.cs` (see the engine README).

## How it works

| File | What it is |
| --- | --- |
| [`SandboxGame.cs`](SandboxGame.cs) | The scene and its tools, a `Game` from the engine |
| [`Toolbar.cs`](Toolbar.cs) | Toolbar layout, hit testing and drawing, in window pixels |
| [`Physics/Body.cs`](Physics/Body.cs) | A rigid box: position, angle, velocities, color, mass and inertia |
| [`Physics/Physics.cs`](Physics/Physics.cs) | The rigid-body simulation: fixed time step and impulse solver |
| [`Physics/Collision.cs`](Physics/Collision.cs) | Box-box contacts with the separating axis theorem |
| [`Program.cs`](Program.cs) | One line: `new SandboxGame().Run();` |

The window, loop, input, camera, colors and quad rendering come from the engine; its [README](../Engine/README.md) explains how they work. The physics is Sandbox's own: it lived in the engine until step 2.5, and moved here because games like the shooter don't use it.

1. **Resize.** `Resized` keeps the camera's aspect ratio in step with the window.
2. **Update.** Sandbox only uses `Update`, not `FixedUpdate`: physics keeps its own 1/240 s fixed step and is fed the real frame time, and the camera should follow the screen smoothly. Each frame, `Physics.Update` advances the bodies first. Then `Esc` and `G` are checked with `Input.WasPressed`, the mouse is handled (below), and WASD or the arrow keys (`Input.IsDown`) move the camera at a constant 1.5 units per second, scaled by delta time, with the direction normalized so diagonals aren't faster.
3. **Drawing.** `Draw` calls `Quads.Begin()`, then two passes with the same quads:
   - **Scene pass:** the camera's `ViewProjection`. Draw every body, then the drag preview if a drag is in progress.
   - **UI pass:** the engine's `ScreenProjection`, which maps window units straight to the screen with (0, 0) at the top left. The toolbar panel, buttons and icons are drawn in those units, so the camera doesn't move them, and last, so they sit on top of the scene.
4. **Toolbar and mouse.** `Toolbar` works in window pixels: the panel, one button per tool, hit testing (`ToolAt`) with plain `Rect`s, and icons built from small rectangles, so there's no texture or text rendering yet. `Toolbar.Draw` paints everything straight into `Quads`, choosing colors for the selected and hovered states, so nothing is allocated per frame. `HandleMouse` in `SandboxGame.cs` does the rest, using `Input.WasPressed` / `WasReleased` for the buttons and `Input.Cursor` for the crosshair:
   - A left click on the panel selects or deselects a tool, or flips the gravity option while the Rectangle tool is selected. Panel clicks never reach the scene.
   - A left click in the scene with the Rectangle tool stores the drag's start in **world** units, using `Input.MouseWorldPosition(camera)`. Each frame, the preview spans from that start to the mouse's current world position, so panning the camera mid-drag keeps the start pinned to the world.
   - Releasing measures the dragged area with `Rect.FromCorners`, which accepts the two corners in any order, and adds a `Body` there that falls or not, depending on the gravity option.

## The physics

Everything in the scene is a `Body`: a box with a `Position`, `Size`, `Angle`, `Velocity`, `AngularVelocity` and `Color`. Its `Model` matrix scales the engine's unit quad to `Size`, rotates it by `Angle`, then moves it to `Position`.

- **Mass.** A falling body gets its mass from its area, and a moment of inertia (resistance to spinning) of `m (w² + h²) / 12`, the formula for a solid rectangle. The solver stores the inverses, so a fixed body is just one with inverse mass 0, that is, infinitely heavy.

1. **The simulation (`Physics.Update`).** A small rigid-body engine modeled on [Box2D Lite](https://github.com/erincatto/box2d-lite), Erin Catto's teaching version of Box2D. `SandboxGame` calls it every frame in `Update` with its list of bodies and the real frame time; it keeps its own, finer fixed step instead of using the engine's `FixedUpdate`.
   - **Fixed time step.** Frame time is collected in an accumulator and simulated in steps of exactly 1/240 s, so results don't depend on frame rate. Small steps also keep a fast fall to a few hundredths of a unit per step, so a box can't skip through a thin platform. After a long hitch, at most 16 steps run and the rest of the time is dropped instead of freezing the app trying to catch up.
   - **Each step** has four phases:
     1. **Gravity.** Every falling body gains downward velocity (9.81 units/s², so one world unit is one meter).
     2. **Contacts.** `Collision.BoxBox` checks every pair with at least one falling body (see below).
     3. **Solver.** 10 passes of sequential impulses over the contacts (see below).
     4. **Integration.** Bodies move and turn with their corrected velocities. This is semi-implicit Euler: velocity first, then position.
   - **Cleanup.** Falling bodies below y = -50 are removed.
2. **Collision (`Collision.cs`).** Rotated boxes are tested with the **separating axis theorem**. Two boxes are apart if, along any of their 4 face directions, their shadows don't overlap. If every direction overlaps, the one with the least overlap is the contact normal.
   - **Reference and incident faces.** The face of the box that owns that direction becomes the reference face. The face of the other box that points most directly back at it is the incident face.
   - **Clipping.** The incident face is cut to the width of the reference face. Each remaining end that is behind the reference face becomes a contact, with a point, a normal and a depth.
   - **Why it matters.** A box lying flat gets 2 contacts, one under each corner, so it rests without rocking. A box hanging over an edge gets its only support off to one side of its center, so gravity turns it and it tips.
3. **Solver (`Physics.cs`).** Each contact applies an **impulse**, an instant change in momentum, at its point. Because the point is off the body's center, the push changes both the velocity and the spin (`AngularVelocity += cross(offset, impulse) / inertia`).
   - **Normal impulse.** Just enough that the two contact points stop approaching. It can push but never pull, so the running total is clamped at 0 or more.
   - **Friction impulse.** Opposes sliding along the surface, capped at `Friction × normal impulse` (Coulomb friction, μ = 0.5). Grip is what makes a box tip over an edge instead of sliding off it.
   - **Overlap correction (Baumgarte).** Overlap deeper than 0.005 units adds a little extra separating speed, removing 20% of it per step, capped at 2 units/s so a box placed inside another is eased out instead of launched.
   - **Warm starting.** A resting contact needs almost the same push every step. Contacts are matched with last step's (same two bodies, point within 0.02 units) and start from last step's totals. Without this, stacked boxes slowly creep and rotate.
   - **Effective mass.** For each contact, `NormalMass` and `TangentMass` are precomputed: how much relative speed a unit push removes at that point, inverted. This includes both bodies' mass and their resistance to spinning around that point.

## Ideas for later

- Zooming the camera (changing `ViewHeight`)
- More tools: select, move and delete rectangles
- Physics extras: bounciness (restitution), per-body friction and density, and sleeping for bodies at rest
- A cloth simulation (Verlet integration and distance constraints) that falls onto the bodies
