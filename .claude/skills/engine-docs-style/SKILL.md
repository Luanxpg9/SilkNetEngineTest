---
name: engine-docs-style
description: How this repo's documentation is written and kept true - the structure of ROADMAP.md and each README, how to write the /// summaries on engine types (with good and bad examples), and how to verify every number against the code or a run. Use when updating docs after a step or phase (the engine-docs agent's job) or writing any README or summary here.
---

# Writing this repo's docs

The project doubles as a learning log, so docs explain *why*, in plain language, and must be true. The owner reads every change.

## Where things go

| File | Holds | Shape |
| --- | --- | --- |
| `ROADMAP.md` | the plan | goal, projects table, steps with phases, checkboxes, **Done when:** per phase. Tick only what the code does; when something moved or proved unnecessary, say so on the item. |
| `README.md` (root) | what the repo is | one paragraph, a projects table, how to run. Keep the table current. |
| `src/Engine/README.md` | the engine guide | file table ("What it is"); usage sections with short examples (Making a game, textures, sprites, entities, collision, logging…); numbered **How it works** explaining mechanisms. |
| `src/TopDownShooter/README.md` | the game | current state, controls table, numbered **How it works**, the art table. |
| `///` on public engine types | the API in the editor | see below |
| `//` in code | why a line is the way it is | short, next to the code |

## `///` summaries

What a game author needs while typing: what it's for, how to use it, and the trap. Not the name restated, and not the implementation (that's `//` or How it works).

Bad:
```csharp
/// <summary>Gets or sets the position.</summary>
/// <summary>Moves the collider.</summary>
```

Good:
```csharp
/// <summary>
/// Where the entity is after the latest fixed step, in world units. Setting it moves the
/// collider too, and the collision world finds it at the new place at once. Setting it is a
/// jump, though: nothing stops it at walls; <see cref="MoveAndSlide"/> does that.
/// </summary>

/// <summary>
/// Fills <paramref name="results"/> (cleared first) with ... Pass in a list kept between frames,
/// so finding doesn't allocate.
/// </summary>
```

- Name the update a call belongs in (`FixedUpdate` for simulation, `Update` per frame) when it matters.
- Use `<see cref=""/>` for engine types and `<paramref>` for parameters; a `cref` that doesn't resolve is a build warning, so build after.
- Units every time: world units, pixels, seconds, radians.

## How to write

- Plain English, short sentences, concrete numbers and real names in backticks.
- Explain a concept once, the first time, in a line.
- Extend the existing structure and tone; don't reorganize unless asked.
- The engine is for any 2D game. Never describe it as top-down only; top-down conveniences are "helpers".
- No filler ("simply", "seamlessly", "powerful"), no emojis.

## Keeping it true

- Read the code before writing about it; examples must compile against the real API (names, parameter order, named arguments).
- Every number (speeds, sizes, draw calls, shape tests, frame counts) comes from the code or from a run. For runtime numbers, run the game and press F3 (see the `run-game` skill); quote what it printed and in what situation.
- When the code and its docs disagree and it's not clear which is wrong, don't change the code: report it.
- Search for stale mentions after a change (`grep` for the old name, number or behavior across `*.md` and `*.cs`).
- Edit only with the Edit and Write tools, one change at a time. Never edit with scripts.
- Build at the end: `dotnet build HavingFunWithEngine.sln`, 0 warnings.
