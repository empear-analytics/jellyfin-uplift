# R4-23 - refactor `MediaBrowser.Model/Dlna/StreamBuilder.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.94** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1046 lines (524 lines of code) |
| Git churn | 380 commits |
| Part | dlna (`MediaBrowser.Model/Dlna`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 47% / 49% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

The score must read 8.94. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- The existing tests call these internal members directly: `ContainerReasons`. Keep each one, with
  its name, signature and class. You may move its body into new private methods.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetTranscodeReasonForFailedCondition` | 133 | 28 | - | - | - |
| 2 | `ApplyVideoTranscodeProfile` | 621 | - | - | - | - |
| 3 | `GetAudioCopyRank` | 741 | - | - | - | - |
| 4 | `GetVideoDirectPlayProfile` | 767 | - | - | - | - |
| 5 | `GetVideoTranscodeProfile` | 653 | - | - | - | - |
| 6 | `SelectDirectPlayMethod` | 918 | - | - | - | - |
| 7 | `SetDirectPlaySubtitle` | 544 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetTranscodeReasonForFailedCondition` (line 133)

- **Recipes G, E, P** - cyclomatic complexity 28; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify GetTranscodeReasonForFailedCondition`

### Step 2: `ApplyVideoTranscodeProfile` (line 621)

- *Excess Number of Function Arguments* (7 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify ApplyVideoTranscodeProfile`

### Step 3: `GetAudioCopyRank` (line 741)

- *Excess Number of Function Arguments* (7 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify GetAudioCopyRank`

### Step 4: `GetVideoDirectPlayProfile` (line 767)

- *Excess Number of Function Arguments* (8 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify GetVideoDirectPlayProfile`

### Step 5: `GetVideoTranscodeProfile` (line 653)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify GetVideoTranscodeProfile`

### Step 6: `SelectDirectPlayMethod` (line 918)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify SelectDirectPlayMethod`

### Step 7: `SetDirectPlaySubtitle` (line 544)

- *Excess Number of Function Arguments* (6 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder): simplify SetDirectPlaySubtitle`

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
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamBuilder.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamBuilder.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-23 (MediaBrowser.Model/Dlna/StreamBuilder.cs)
Baseline score: 8.94
Final score:    9.68
Steps completed: 1 of 7 (step 1, GetTranscodeReasonForFailedCondition, Recipe E rule 11 chain; steps 2-7 not attempted, see below)
Steps reverted:  0 of the committed step. Measured and not kept (score in brackets): one helper of the last 3, 4, 5, 6 cases (9.02, 9.05, 9.07, 9.10, UP but below the chain); one helper of the last 7 to 14 cases (8.89, DOWN, the helper stays Complex Method); chains of 4-case groups (8.81, DOWN: Code Duplication, six helpers), 5-case groups (8.81, DOWN), 7-case groups (8.97, UP), 8-case groups (8.89, DOWN), 9 and 13 (8.84 and 8.89, DOWN); mixed partitions 6,7,7,6 and 7,7,6,6 (9.20, UP), 8,6,6,6 and 6,6,6,8 (9.38, UP, with one Complex Method left). Chosen: 2,6,6,6,6 (cases from the end in six-case groups), 9.68 UP with no Complex Method; 6,6,6,6,2 and 5,6,6,6,3 tie at 9.68 and were not chosen.
Smells left without a recipe: Excess Number of Function Arguments on six private methods (SetDirectPlaySubtitle 6, ApplyVideoTranscodeProfile 7, GetVideoTranscodeProfile 5, GetAudioCopyRank 7, GetVideoDirectPlayProfile 8, SelectDirectPlayMethod 5). Not attempted in this run: Recipe E rule 12 only gives the new helper at most four parameters and leaves the original method's count unchanged, and no recipe changes a private signature, so these cannot clear within the catalogue.
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, same as base)
Tests: PASS (dotnet test Jellyfin.sln --no-build: every project passed, 0 failed; Model 808 passed, Integration 429 passed and 3 skipped (Skip attributes in source), Controller 1411 passed)
Commits: 4952589f6d refactor(StreamBuilder): simplify GetTranscodeReasonForFailedCondition (8.94 to 9.68, UP), plus the commit that fills in this report
Unsure about: (1) Helper names: contiguous groups mix properties, so each helper is named by its first property (TranscodeReasonForAudioProfileCondition, TranscodeReasonForIsInterlacedCondition, TranscodeReasonForRefFramesCondition, TranscodeReasonForVideoLevelCondition); no name clashes in any StreamBuilder*.cs part. (2) Four rule 11 applications in one commit, allowed by rule 1 (same recipe, same method). (3) Recipes G, P and rule 9 were not applied: this method has no nesting, no compound condition and no run of statements to extract. (4) Setup slip: a mis-expanded path created an empty folder inside the worktree; I removed it, and the worktree is clean. Scratch folder is %TEMP%\h-R4-23, deleted at the end.
```
