# R17 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.54** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 722 lines (311 lines of code) |
| Git churn | 86 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | D |
| Smells without a recipe | Excess Number of Function Arguments, Primitive Obsession, String Heavy Function Arguments |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 69% / 64% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs
```

The score must read 8.54. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Code Duplication | 2 | D |
| Excess Number of Function Arguments | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |
| String Heavy Function Arguments | 2 | none - report it |

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetRequestedAudioBitDepth` | 682 | - | - | - | - |
| 2 | `GetRequestedMaxRefFrames` | 644 | - | - | - | - |
| 3 | `GetRequestedVideoBitDepth` | 663 | - | - | - | - |
| 4 | `ReportTranscodingProgress` | 730 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetRequestedAudioBitDepth` (line 682)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(EncodingJobInfo): simplify GetRequestedAudioBitDepth`

### Step 2: `GetRequestedMaxRefFrames` (line 644)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(EncodingJobInfo): simplify GetRequestedMaxRefFrames`

### Step 3: `GetRequestedVideoBitDepth` (line 663)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(EncodingJobInfo): simplify GetRequestedVideoBitDepth`

### Step 4: `ReportTranscodingProgress` (line 730)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingJobInfo): simplify ReportTranscodingProgress`

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
dotnet test tests/Jellyfin.Controller.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --dedup --together MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R17 (MediaBrowser.Controller/MediaEncoding/EncodingJobInfo.cs)
Baseline score: 8.54
Final score:    <x.xx>
Steps completed: <n> of 4
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
