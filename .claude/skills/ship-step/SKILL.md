---
name: ship-step
description: How a finished roadmap step or phase is committed, merged into main, tagged and released on GitHub in this repo, including version numbers and release notes. Use only when the user asks to commit, ship, merge or release a step or phase.
---

# Shipping a step or phase

Only when the user asks. Each step is: commit on the branch, merge into `main`, tag, push, release.

## Version numbers

- Step N is `v0.N.x`. Its first phase (or the whole step, if it has no phases) is `v0.N.0`; each later phase or follow-up bumps the last number: step 5 Phase A `v0.5.0`, Phase B `v0.5.1`, fixes after that `v0.5.2`.
- `v0.N.5` is kept for half steps like step 2.5.
- Never move a published tag; make a new version instead.
- Check what exists first: `git tag --sort=-creatordate | head`.

## Steps

1. **Commit on the phase branch** (`step-6-phase-a`). Stage the files by name, never blindly. The message: a title like `Step 6 Phase A: <phase name>`, then short sections (Engine, Shooter, Docs) listing what changed, then the attribution line from the session's instructions.
2. **Merge into main**, oldest unshipped phase first:
   ```bash
   git switch main
   git merge --no-ff step-6-phase-a -m "Merge step 6 Phase A: <phase name>"
   git tag -a v0.6.0 -m "v0.6.0: step 6 Phase A, <phase name>"
   ```
   If an earlier phase of the step was never merged, merge and tag it first, with its own version, then the next one.
3. **Build `main`** after merging: `dotnet build HavingFunWithEngine.sln`, 0 warnings.
4. **Push** main, the branches and the tags in one go:
   ```bash
   git push origin main step-6-phase-a v0.6.0
   ```
5. **Release on GitHub.** `gh` isn't on Git Bash's PATH; call it from PowerShell by its full path. Write the notes to a file in the scratchpad first:
   ```powershell
   & "C:\Program Files\GitHub CLI\gh.exe" release create v0.6.0 --repo Luanxpg9/SilkNetEngineTest --title "v0.6.0: <name>" --notes-file "<scratchpad>\notes-v0.6.0.md" --latest
   ```
   Pass `--latest` only on the newest version of the batch.

## Release notes

- First line: `Step N Phase X: <what it delivers>`, and say when it completes the step.
- `## Engine` and `## <Game>` sections with bold lead-ins per item, in plain language, for someone who'll play or use it rather than read the diff.
- Mention measured numbers (F3) where they show the point.
- End with what reviewed it ("Reviewed by the silk-engine agent, with its fixes included.").

## Afterwards

Tell the user the commit, merge and tag names, link the release URLs that `gh` printed, and say what's next on the roadmap.
