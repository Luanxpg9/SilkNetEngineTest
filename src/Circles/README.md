# Circles

A side experiment: what would a renderer built on circles instead of triangles look like?

The GPU can only fill triangles, so every circle is still a quad made of 2 triangles. The circle itself comes from math in the fragment shader, a technique called **impostors**. Thousands of circles are drawn with **one instanced draw call**, and a uniform grid handles **ball-to-ball collisions** on the CPU.

```bash
dotnet run --project src/Circles
```

## Controls

| Key | Action |
| --- | --- |
| `1` | Flat, anti-aliased circles |
| `2` | Lit sphere impostors |
| `↑` / `↓` | Double / halve the ball count (1 to 262,144) |
| `C` | Toggle collisions |
| `V` | Toggle vsync (turn off to see real FPS) |
| `Space` | Pause |
| `Esc` | Quit |

The window title shows the count, mode, FPS, and collision and vsync state.

## How it works

### Instanced rendering ([`Program.cs`](Program.cs))

- One quad mesh: 4 corners and 6 indices in an element buffer.
- A second buffer holds per-ball data, a packed `CircleInstance` struct with center, radius and color (28 bytes). The C# array goes to the GPU directly with `BufferSubData`, with no conversion.
- `VertexAttribDivisor(location, 1)` makes those attributes advance once per instance instead of once per vertex.
- A single `DrawElementsInstanced` call draws every ball.

### Circles in the fragment shader

The vertex shader gives each pixel its position inside the quad, where length 1.0 is the edge of the circle.

- **Flat mode:** pixels outside the circle are discarded. `fwidth` and `smoothstep` fade the edge over about one pixel, so circles look smooth at any size.
- **Sphere mode:** each circle is treated as the outline of a ball. The shader recovers the surface height with `h = sqrt(1 - d²)`, uses it as a normal for diffuse and specular lighting, and writes it to `gl_FragDepth`. Overlapping spheres cut into each other like real 3D balls, with no tessellation.

Writing `gl_FragDepth` turns off the GPU's early depth test. That is the performance cost of the sphere effect.

### Collisions ([`CollisionGrid.cs`](CollisionGrid.cs))

- **Broad phase:** the screen is divided into cells as wide as the largest ball. Each frame, balls are bucketed into cells with a counting sort (count, prefix sum, scatter), with no allocations. Each ball then checks only the 3×3 cells around it instead of every other ball.
- **Narrow phase:** overlapping balls are pushed apart, with the lighter one moving more (mass = radius²). Then an impulse along the contact normal bounces them, but only if they are moving toward each other.
- `Restitution = 1` gives perfectly elastic bounces. Lower it to make the system lose energy and settle.

## Known limitations

- At high counts the balls pack together and jitter. A single resolve pass per frame can't settle a ball pushed from several sides at once. Running several passes per frame (substeps) would fix it.
- Physics and the per-frame upload run on the CPU, which is the bottleneck at high counts. Moving them to the GPU would need compute shaders (OpenGL 4.3) or transform feedback.
- With collisions on, balls never overlap on screen, so the sphere intersection effect only shows with collisions off.
