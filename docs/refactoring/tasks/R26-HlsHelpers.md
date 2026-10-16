# R26 - refactor `Jellyfin.Api/Helpers/HlsHelpers.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.38** / 10 (Green) |
| Exit target | **10.00** |
| File size | 132 lines (59 lines of code) |
| Git churn | 21 commits |
| Part | api (`Jellyfin.Api/Helpers`) |
| Recipes needed | G |
| Smells without a recipe | none |
| Suitable for | mid-capability agent or better |
| Mutation score (Stryker / Mull) | 81% / 75% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/HlsHelpers.cs
```

The score must read 9.38. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Deep, Nested Complexity | 3 | G |

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `WaitForMinimumSegmentCount` | 27 | - | 4 | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `WaitForMinimumSegmentCount` (line 27)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.

Commit message: `refactor(HlsHelpers): simplify WaitForMinimumSegmentCount`

### Final step: re-measure, then stop

Once the score reaches 10.00, **stop**.

## 7. Verification - run all four after EVERY step

**1 - it still builds, warnings included.**

```bash
dotnet build Jellyfin.sln
```

Zero errors, and no new warning in the file you changed.

**2 - the tests that cover this part still pass.**

```bash
dotnet test tests/Jellyfin.Api.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Jellyfin.Api/Helpers/HlsHelpers.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/HlsHelpers.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Helpers/HlsHelpers.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Jellyfin.Api/Helpers/HlsHelpers.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R26 (Jellyfin.Api/Helpers/HlsHelpers.cs)
Baseline score: 9.38
Final score:    <x.xx>
Steps completed: <n> of 1
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
