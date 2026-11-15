# Top-Down Shooter

The first game built on [`src/Engine`](../Engine). Each step of the [roadmap](../../ROADMAP.md) adds to it, until it's a playable top-down shooter.

```bash
dotnet run --project src/TopDownShooter
```

## Current state: walking and aiming

A green square, the player, stands in a checkered arena 30×20 units across, with a grey wall around it. The white bar on its front edge shows which way it faces.

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` / arrow keys | Move (5 units per second, the same diagonally) |
| Mouse | Aim: the player turns to face the cursor |
| `Esc` | Quit |

The camera follows the player, so the player stays in the middle of the screen and the arena slides underneath. The player can't leave the arena. For now that's a simple clamp; real collision comes in roadmap step 5.

## How it works

[`ShooterGame.cs`](ShooterGame.cs) is the game, a `Game` from the engine. [`Player.cs`](Player.cs) is the player. [`Program.cs`](Program.cs) just runs the game (and has a commented-out line for hiding info logs).

1. **Movement in the fixed step.** `FixedUpdate` runs exactly 60 times per second, however fast the screen is. It reads the held keys (`Input.IsDown`) into a direction, and `Player.Move` normalizes it and moves the player by `Speed × step`. Then the position is clamped inside the walls, inset by half the player's size.
2. **Smooth drawing (interpolation).** On a 144 Hz screen, a player that only moves 60 times per second would look jumpy. So `Player` keeps both its latest position and the one from the step before, and `DrawPosition(alpha)` blends between them. The engine's `FixedStepAlpha` says how far the clock is into the next step (0 to 1). The player is drawn slightly in the past, at most one step (1/60 s), but it moves smoothly at any refresh rate.
3. **Camera and aim, per frame.** `Update` runs once per frame. It checks `Esc` there because that's a one-off press. Then it:
   - centers the camera on the player's draw position, so the two always match and the player doesn't shake on screen;
   - converts the mouse to world units with that camera (`Input.MouseWorldPosition`), and turns the player toward it with `Atan2`. When the mouse is right on the player, the last angle is kept.
4. **Drawing.** `Draw` uses the engine's `Quads`. The floor is 600 one-unit tiles in two shades, so you can see the movement while the camera follows, plus four wall strips. The player is two quads with the same rotation and position: the body, and the nose, which is moved to the front edge *before* the turn so it always ends up on the side the player faces.
   - That's about 600 draw calls a frame, fine for now. The sprite batcher in roadmap step 3 will draw them in one.
