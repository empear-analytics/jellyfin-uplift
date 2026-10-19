# R4-36 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.26** / 10 (Green) |
| Exit target | **10.00** |
| File size | 214 lines (129 lines of code) |
| Git churn | 12 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs
```

The score must read 9.26. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetAmdDx11VidFiltersPrefered` | 67 | 18 | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetAmdDx11VidFiltersPrefered` (line 67)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 18): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.AmdFilters): simplify GetAmdDx11VidFiltersPrefered`

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-36 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.AmdFilters.cs)
Baseline score: 9.48 (HEAD at start; the 9.26 above is stale, the coordinator gave 9.48)
Final score:    9.52
Steps completed: 1 of 1 (software-decoder block, rule 12 second struct AmdSecondSwDecoderContext)
Steps reverted:  2 (d3d11va candidate, multi-line layout: 9.46 DOWN; sw candidate, multi-line layout: 9.46 DOWN)
Smells left without a recipe: NONE without a recipe. Complex Method cc 13 in GetAmdDx11VidFiltersPrefered remains: the d3d11va block is blocked (rule 12 allows two structs per method and the method has two; a shared decoder struct would be 13 members, see Unsure about)
Build: PASS (0 errors, 211 warnings, none in the file)
Tests: PASS (Controller 1411/1411, Integration 429 passed 3 skipped, full solution 0 failed)
Commits: 30ee3fbfab (software-decoder block); plus a docs-only commit for this report block
Unsure about: (1) Layout matters for the score: the same statement with the 12-argument construction on 14 lines scores 9.46 DOWN, on one line 9.52 UP. Diagnostics on scratch copies (not committed): struct alone and unused helper alone both 9.48; the block removed with no call site 9.53; the construction replaced by default(...) 9.52. The score seems to track the method's line count, not the argument count. The playbook is silent on layout. (2) The d3d11va candidate on one line also measured 9.52 UP, a tie with the sw candidate; I committed the sw candidate, as it came first in the task. Either alone is valid, not both (a third struct is not allowed). (3) The baseline 9.26 in this file is stale; I proceeded at 9.48 as the coordinator stated. (4) Not measured, not committed, for ruling only: one struct for both decoder blocks would have 13 members when each guard flag moves into its helper as a parameter (9 shared values, 2 software-only: VidEncoder, ThreeDFormat; 2 d3d11va-only: DoOclTranspose, TransposeDir), above the limit of 12, and the helpers have no free parameter slot. Leaving the guard at the call site would give 11, but rule 12 says to move the whole statement, so I did not pursue it.
```
