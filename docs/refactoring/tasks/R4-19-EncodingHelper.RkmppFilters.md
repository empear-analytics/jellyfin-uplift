# R4-19 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.78** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 323 lines (175 lines of code) |
| Git churn | 6 commits |
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
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs
```

The score must read 8.78. If it does not, this file changed after the task was
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
| 1 | `GetRkmppVidFiltersPrefered` | 71 | 40 | - | 4 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetRkmppVidFiltersPrefered` (line 71)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 40): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.RkmppFilters): simplify GetRkmppVidFiltersPrefered`

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-19 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.RkmppFilters.cs)
Baseline score: 8.78 (task file); 9.11 at the start of round 5 (HEAD 5338b1c343), as the coordinator stated
Final score:    9.11 (no step kept)
Steps completed: 0 of 1
Steps reverted:  1 (Recipe E rule 12, second context struct; two candidates, both UP alone but not kept)
  - sw-decoder family, RkmppSecondSwDecoderContext (12 members), helper RkmppSecondAddSwDecoderFilters
    in EncodingHelper.RkmppFilterSteps.cs: 9.12 UP (Bumpy Road 3 -> 2, Complex Method cc 27 -> 26).
  - hw-scale family, RkmppSecondHwScaleContext (12 members), helper RkmppSecondAddHwScaleFilters
    in EncodingHelper.RkmppFilterSteps.cs: 9.13 UP (Bumpy Road stays at 3, cc 27 -> 24). Best of the two.
  - Same two with the helper inside EncodingHelper.RkmppFilters.cs: sw 8.31 DOWN, hw 8.32 DOWN
    (new Overall Code Complexity and Primitive Obsession on the file).
  - Why not kept: rule 12 says to move the WHOLE statement that holds the block, guard included.
    For both families that needs 16 values (sw: the flag, state, options, mainFilters and 12 others;
    hw: the four guard values, the ten other block values, mainFilters and hwScaleFilter) against the cap of 12
    struct members plus 3 plain parameters (4 parameters in all, the context included) = 15.
    Both candidates therefore leave the guard at the call site, which the playbook says keeps the
    bumps (the hw candidate kept all three). That is not the recipe as written, so it is reported
    as a candidate, not committed.
  - Candidate patches (not in any commit): the struct, the helper and the call site as above.
Smells left without a recipe: NONE stated. Left with recipe-level candidates only:
  - Bumpy Road Ahead (3 bumps): rule 12 not applicable as written (see above); not tried: Recipe E
    rule 13 on the `else if (isRkmppDecoder)` tail, since that branch assigns no variable (rule 13
    lists only the assign and return forms).
  - Complex Method (cc 27): not measured in this run (only the struct candidates were in scope).
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, same as base)
Tests: PASS (dotnet test Jellyfin.sln --no-build: all 17 test assemblies, 0 failed, 0 failing tests)
Commits: <sha of the report commit, docs only>
Unsure about:
  - Whether a guard left at the call site is acceptable under rule 12 when the whole statement
    exceeds the struct cap (the playbook needs an explicit ruling; this run did not decide it).
  - Helper placement in EncodingHelper.RkmppFilterSteps.cs instead of after the current method: the
    task allowed it, and it is what made both candidates UP (the file-level smells disappear).
  - The task file baseline (8.78) is stale; the file scores 9.11 at HEAD 5338b1c343.
```
