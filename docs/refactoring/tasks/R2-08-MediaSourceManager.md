# R2-08 - refactor `Emby.Server.Implementations/Library/MediaSourceManager.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **7.78** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1161 lines (589 lines of code) |
| Git churn | 190 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments, Low Cohesion, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 45% / 40% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/MediaSourceManager.cs
```

The score must read 7.78. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Low Cohesion | 3 | none - report it |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetPathProtocol` | 250 | 9 | - | - | - |
| 2 | `AddMediaInfoWithProbe` | 841 | - | - | - | - |
| 3 | `GetMediaSource` | 381 | - | - | - | - |
| 4 | `GetPlaybackMediaSources` | 177 | - | - | - | - |
| 5 | `MediaSourceManager` | 67 | - | - | - | - |
| 6 | `SetDefaultAudioStreamIndex` | 556 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetPathProtocol` (line 250)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(MediaSourceManager): simplify GetPathProtocol`

### Step 2: `AddMediaInfoWithProbe` (line 841)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(MediaSourceManager): simplify AddMediaInfoWithProbe`

### Step 3: `GetMediaSource` (line 381)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(MediaSourceManager): simplify GetMediaSource`

### Step 4: `GetPlaybackMediaSources` (line 177)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(MediaSourceManager): simplify GetPlaybackMediaSources`

### Step 5: `MediaSourceManager` (line 67)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(MediaSourceManager): simplify MediaSourceManager`

### Step 6: `SetDefaultAudioStreamIndex` (line 556)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(MediaSourceManager): simplify SetDefaultAudioStreamIndex`

### File-level smells

- *Low Cohesion*: no recipe in the playbook. Report it.
- *Primitive Obsession*: no recipe in the playbook. Report it.

### Final step: re-measure, then stop

Once the score reaches 10.00, **stop**.

## 7. Verification - run all four after EVERY step, before committing it

The tools compare your working tree with `HEAD`, so run them before `git commit`. To
re-check a commit you already made, add `--base HEAD~1`.

**1 - it still builds, warnings included.**

```bash
dotnet build Jellyfin.sln
```

Zero errors, and no new warning in the file you changed.

**2 - the tests that cover this part still pass.**

```bash
dotnet test tests/Jellyfin.Server.Implementations.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Emby.Server.Implementations/Library/MediaSourceManager.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/MediaSourceManager.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/MediaSourceManager.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Emby.Server.Implementations/Library/MediaSourceManager.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R2-08 (Emby.Server.Implementations/Library/MediaSourceManager.cs)
Baseline score: 7.78
Final score:    <x.xx>
Steps completed: <n> of 6
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
