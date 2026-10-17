# R2-23 - refactor `MediaBrowser.Model/Dlna/StreamInfo.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.09** / 10 (Green) |
| Exit target | **10.00** |
| File size | 1497 lines (540 lines of code) |
| Git churn | 182 commits |
| Part | dlna (`MediaBrowser.Model/Dlna`) |
| Recipes needed | none |
| Smells without a recipe | Excess Number of Function Arguments, Primitive Obsession, String Heavy Function Arguments |
| Suitable for | any agent |
| Mutation score (Stryker / Mull) | 52% / 74% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

The score must read 9.09. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Excess Number of Function Arguments | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |
| String Heavy Function Arguments | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `AddSelectedSubtitleProfiles` | 1261 | - | - | - | - |
| 2 | `AddSubtitleProfiles` | 1299 | - | - | - | - |
| 3 | `AddSubtitleStreamInfoPerProfile` | 1315 | - | - | - | - |
| 4 | `AddUnselectedSubtitleProfiles` | 1275 | - | - | - | - |
| 5 | `GetSubtitleProfiles` | 1239 | - | - | - | - |
| 6 | `GetSubtitleStreamInfo` | 1327 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `AddSelectedSubtitleProfiles` (line 1261)

- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify AddSelectedSubtitleProfiles`

### Step 2: `AddSubtitleProfiles` (line 1299)

- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify AddSubtitleProfiles`

### Step 3: `AddSubtitleStreamInfoPerProfile` (line 1315)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify AddSubtitleStreamInfoPerProfile`

### Step 4: `AddUnselectedSubtitleProfiles` (line 1275)

- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify AddUnselectedSubtitleProfiles`

### Step 5: `GetSubtitleProfiles` (line 1239)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify GetSubtitleProfiles`

### Step 6: `GetSubtitleStreamInfo` (line 1327)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify GetSubtitleStreamInfo`

### File-level smells

- *Primitive Obsession*: no recipe in the playbook. Report it.
- *String Heavy Function Arguments*: no recipe in the playbook. Report it.

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
dotnet test tests/Jellyfin.Model.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamInfo.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamInfo.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R2-23 (MediaBrowser.Model/Dlna/StreamInfo.cs)
Baseline score: 9.09
Final score:    <x.xx>
Steps completed: <n> of 6
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
