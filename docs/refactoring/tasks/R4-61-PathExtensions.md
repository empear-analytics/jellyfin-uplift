# R4-61 - refactor `Emby.Server.Implementations/Library/PathExtensions.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.68** / 10 (Green) |
| Exit target | **10.00** |
| File size | 145 lines (55 lines of code) |
| Git churn | 43 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | P |
| Smells without a recipe | none |
| Suitable for | any agent |
| Mutation score (Stryker / Mull) | 86% / 84% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/PathExtensions.cs
```

The score must read 9.68. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Complex Conditional | 2 | P |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `TryReplaceSubPath` | 45 | - | - | - | 2 |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `TryReplaceSubPath` (line 45)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(PathExtensions): simplify TryReplaceSubPath`

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
python tools/refactor_guard.py Emby.Server.Implementations/Library/PathExtensions.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/PathExtensions.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/PathExtensions.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Emby.Server.Implementations/Library/PathExtensions.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-61 (Emby.Server.Implementations/Library/PathExtensions.cs)
Baseline score: 9.68
Final score:    9.68
Steps completed: 0 of 1
Steps reverted:  1 (Step 1, TryReplaceSubPath, Recipe P: no candidate scored UP; each candidate measured alone and reverted)
  C1 whole condition of the `if` at line 45 as PathIsNotAtSubPathBoundary(4 args): 9.38 DOWN (adds Primitive Obsession, String Heavy Function Arguments)
  C2 leading two terms of the `&&` chain as PathIsNotLongerThanUnterminatedSubPath(3 args), last term kept at call site: 9.38 DOWN (same two smells)
  C3 leading three terms of the `||` chain in IsMissingOrLongerThanPath as PathIsNotAllPresent(3 args): 9.09 DOWN (Complex Conditional at TryReplaceSubPath:45 still flagged, plus the same two smells)
Smells left without a recipe: NONE. The one smell left, Complex Conditional at TryReplaceSubPath:45 (2 expressions, lines 45-47), was tried under Recipe P (C1, C2) and has no UP application: each predicate brings Primitive Obsession and String Heavy Function Arguments, because its parameters are strings, a bool and a char.
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, same as base)
Tests: FAIL on one test, classified as a flake. Full suite: Jellyfin.Providers.Tests.MediaInfo.AtlFlacRegressionTests.AtlParser_MalformedFlac_DoesNotHang timed out after 5000 ms under concurrent load (run on the unchanged start commit). Run alone once: passed (84 ms). All other test projects passed (Jellyfin.Server.Implementations.Tests 1102 passed, 17 skipped; Jellyfin.Server.Integration.Tests 429 passed, 3 skipped).
Commits: no code commit (no candidate scored UP); docs-only commit of this report block: see git log
Unsure about: whether the one-time solo re-run of the timed-out test is an acceptable classification under the "passes alone after failing under load" rule (it is the only re-run made); no other doubts. Candidate for a new recipe, not applied: none identified; the playbook allows no predicate with fewer primitive parameters for this condition.
```
