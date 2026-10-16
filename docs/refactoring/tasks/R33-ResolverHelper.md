# R33 - refactor `Emby.Server.Implementations/Library/ResolverHelper.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.84** / 10 (Green) |
| Exit target | **10.00** |
| File size | 168 lines (62 lines of code) |
| Git churn | 55 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 69% / 53% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/ResolverHelper.cs
```

The score must read 9.84. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `EnsureDates` | 106 | - | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `EnsureDates` (line 106)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(ResolverHelper): simplify EnsureDates`

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
dotnet test tests/Jellyfin.Server.Implementations.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Emby.Server.Implementations/Library/ResolverHelper.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/ResolverHelper.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/ResolverHelper.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> Emby.Server.Implementations/Library/ResolverHelper.cs` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R33 (Emby.Server.Implementations/Library/ResolverHelper.cs)
Baseline score: 9.84
Final score:    <x.xx>
Steps completed: <n> of 1
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
