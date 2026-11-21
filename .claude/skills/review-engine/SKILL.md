---
name: review-engine
description: Checklist for reviewing changes to this repo's engine and games (C#, Silk.NET, OpenGL 3.3) - the bugs past reviews actually found, how to verify them, and the report format. Use when reviewing a step or phase before it ships (the silk-engine agent's review job), or when hunting a bug in src/Engine or a game.
---

# Reviewing engine and game changes

## Get the change

- `git status`, `git diff` (uncommitted), `git diff main...HEAD` (the branch). Read the whole changed files, not only the hunks, and the engine types they call.
- Build: `dotnet build HavingFunWithEngine.sln` — 0 warnings is the bar (nullable and XML-doc warnings count).
- Read only unless asked to fix. Don't commit.

## Where bugs have been found before

Check these first; each one has bitten this repo.

- **Smooth motion.** Entities move in `FixedUpdate`, draw at `DrawPosition` (a blend of `PreviousPosition` and `Position`). The scene copies `Position` into `PreviousPosition` before the step. Look for: things drawn at `Position` instead of `DrawPosition`; spawns or teleports that don't set both positions; a new entity that stands still for a step (spawned mid-pass, first moves next step); anything per-frame that reads the real position while the sprite is drawn blended.
- **Changes during a pass.** `Scene` queues spawns and flags destroys during `FixedUpdate`/`Update`/`Draw`; applied when the pass ends. A destroyed entity is skipped at once, including in `Draw` (so it's never drawn again). Look for code assuming a spawned entity runs this pass, or a destroyed one is still drawn.
- **Input timing.** `WasPressed` works in both `Update` and `FixedUpdate`, once per press in each. One-off actions (quit, toggles) in `Update`; simulation input (movement, firing) in `FixedUpdate`.
- **Collision.**
  - Contact normals point away from the other collider, toward the mover.
  - Masks are one-way: a collider notices another when its mask has the other's layer.
  - The grid must see every move: positions change through `Entity.Position`, `Collider.Position` or `Collider.Shape`, which refile.
  - Look for colliders moved some other way, queries returning destroyed colliders, rays that start inside a shape (distance 0), fast things that can skip thin ones, and push-out that can leave a body inside a wall.
- **Allocations in hot paths.** No `new`, LINQ, closures, `params` arrays, boxing or string building per frame or per query. Lists are filled by the caller and kept between frames. Lambdas passed per frame must be `static`.
- **GL state.** `SpriteBatch.Begin` sets the program, VAO, blending (`One, OneMinusSrcAlpha`, premultiplied), no culling, no depth test, every time. `End` unbinds the VAO. GL objects created must be deleted in `Dispose`/`Unload`, once. Textures are premultiplied on load; tints are premultiplied in `Draw`; `glow` only lowers alpha.
- **Units and numbers.** World units (y up) vs window pixels (y down). Check numbers quoted in comments and READMEs against the code (speeds, sizes, draw-call counts, step lengths: one fixed step is 1/60 s).
- **Docs vs code.** A `///` summary or README sentence that's no longer true is a finding.
- **Generic engine.** Anything shooter-specific or top-down-only that crept into `src/Engine`.

## Verify, don't guess

- Trace a suspected bug through the real code path, with concrete values, before reporting it.
- If running the game would settle it, say what to run (see the `run-game` skill) and what the log or a screenshot should show.
- A finding needs a failure scenario: the input or situation, and what goes wrong.

## Report

Ranked, most severe first, under about 700 words:

```
N. **Bug | Risk | Nit.** `path/File.cs:line` — what's wrong.
   - Failure: the concrete situation and its effect.
   - Fix: the exact change, code-level and short.
```

- **Bug:** wrong behavior now. **Risk:** wrong in a reachable case or a coming step. **Nit:** clarity, comments, style.
- End with "Checked and fine": what you verified and found correct, so the owner knows what was covered.
- If the user asked for fixes to be applied, every suggestion will be acted on, so only suggest what you'd stand behind.
