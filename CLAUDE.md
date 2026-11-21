# HavingFunWithEngine

A small 2D game engine built from scratch in C# (.NET 9) on Silk.NET and raw OpenGL 3.3, and the games built on it. `ROADMAP.md` is the plan; the READMEs in each project explain how things work.

## Working on a roadmap step

Every roadmap step goes through the same three agents, in this order. The agents live in `.claude/agents/`.

### 1. Plan it with `engine-architect`

Before writing code for a step, run the `engine-architect` agent on it. It reads the step in `ROADMAP.md`, checks it against the engine as it is now and the steps that come after, and:

- says whether the step is the right size, and if it's too big for one pass, proposes how to break it into phases (engine side first, then the game using it, each phase ending with something you can run and a **Done when:** line);
- flags items that would make the engine less generic (anything that only suits a top-down game) or paint later steps into a corner, with a better way to do them.

Apply its plan to `ROADMAP.md` (the phase split, reworded items) and show the user before starting on the code. The architect is read-only; its plan goes into the roadmap through the main session.

### 2. Build it, then review with `silk-engine`

Implement the planned step (or phase) on its own branch, build it, and run the shooter to check it really works. Then run the `silk-engine` agent to review the changes, and apply the fixes it suggests. If a suggestion seems wrong, say why instead of silently skipping it. Build and run the game again after the fixes.

### 3. Document it with `engine-docs`

Run the `engine-docs` agent to bring `ROADMAP.md`, the READMEs and the `///` summaries on the engine's public types in line with the code. Check its report: it lists what it changed and any code that disagrees with its docs, which needs a decision. Then commit.

## Skills

The project's skills in `.claude/skills/` back up each stage. The agents load theirs; the main session uses the rest.

| Skill | For | Used by |
| --- | --- | --- |
| `plan-step` | the roadmap's format, when to split a step into phases, the checks before building | `engine-architect`, and the main session when it edits a step |
| `run-game` | building and driving a game's window from a script: keys, aiming, clicks, screenshots, the console log | the main session after building, `silk-engine`, `engine-docs` (to measure numbers) |
| `review-engine` | where bugs have been found before, and the review report format | `silk-engine` |
| `engine-docs-style` | the structure of each doc and how to write `///` summaries | `engine-docs` |
| `ship-step` | commit, merge, tag, push and release, with version numbers and release notes | the main session, when the user asks to ship |

## Rules for every change

- Edit project files only with the Edit and Write tools, one clear change at a time, so every change is a readable diff. Never edit files with Python, sed, PowerShell or other scripts. Scripts are fine for testing: building, driving the game window, screenshots.
- The engine (`src/Engine`) stays generic: nothing specific to the shooter, and nothing that assumes a top-down game. Top-down conveniences can be added as helpers on top.
- Each step or phase gets its own branch (`step-5-phase-a`). Commit, merge and release only when the user asks, following the `ship-step` skill.
