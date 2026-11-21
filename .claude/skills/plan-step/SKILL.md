---
name: plan-step
description: How to plan a roadmap step in this repo before coding it - read the step, check it keeps the engine generic and extensible, split it into phases when it's too big, and write it in ROADMAP.md's format with a Done when line per phase. Use when planning or splitting a step (the engine-architect agent's main job), or when rewriting a step in ROADMAP.md.
---

# Planning a roadmap step

## What a well-planned step looks like

Steps in `ROADMAP.md` follow this shape. Keep it exactly (headings, checkbox style, bold **Done when:**):

```markdown
### 6. Tilemap levels
One or two sentences: what the step is for, and any principle it must follow.

#### Phase A: <short name>
One line: what this phase delivers and why it comes first.
- [ ] An item: one thing to build, specific enough to check off, with the reason when it isn't obvious
- [ ] Shooter: an item that uses the new engine piece in the game

**Done when:** something you can run and see, stated so anyone can check it.

#### Phase B: <short name>
...
```

- **Split when** the step mixes engine work and game work, has more than about 7 items, or contains an item that's a project of its own. The usual split is engine side first (with a small proof in the game), then the game putting it to work. Look at steps 2 to 5 in `ROADMAP.md` for the pattern.
- **Every phase ends with something runnable.** A phase that only adds engine code still needs a visible proof in a game (step 5 Phase A made the walls solid).
- **Done when** describes behavior you can watch or measure ("the soldier slides along the walls", "F3 shows the shape tests staying low with a dozen bullets"), not "the code exists".
- **Items name real things**: the type or file they extend, units (world units, pixels, seconds), and which game uses them. Prefix game items with the game ("Shooter: ...").

## Checks before building

Go through each item and ask:

1. **Generic?** Would a platformer, puzzle game or side view use it unchanged? Flag anything that assumes gravity, an "up" direction, a top-down camera, one player, or the shooter's names and sizes. Top-down conveniences are fine as helpers on top of a generic core.
2. **Where does it live?** Engine (`src/Engine/<Area>/`) or game (`src/<Game>/`)? Engine areas: `Graphics/`, `Input/`, `Entities/`, `Collision/`, plus `Game.cs`. A new area needs a reason.
3. **Does it fit what's there?** Reuse existing patterns: entities owned by `Scene`, simulation in `FixedUpdate`, `Draw` at `DrawPosition`, lists filled by the caller, `IDisposable` for GL resources, sprites through `SpriteBatch` layers.
4. **What do later steps need from it?** Read the steps after it. Will tilemaps, the HUD, audio or the game have to rewrite this? If an easy change now saves a rewrite later, put it in; if it's speculative, leave it out.
5. **Anything missing?** An item that silently needs another piece (a raycast needs a world to cast in). Anything listed that really belongs in a later step?
6. **Testable?** Can it be checked by running the game (see the `run-game` skill)? If not, add a visible proof (a debug key, an F3 number).

## What to hand back

- A short verdict: ready as written, or not and why.
- The step rewritten in the format above, ready to paste into `ROADMAP.md`.
- One line per change explaining why.
- Design notes for whoever builds it: where the code goes, the API you'd expect (names, signatures), and the risks.

Don't tick boxes or change other steps unless the plan needs it (an item moving to a later step); say so when it does.
