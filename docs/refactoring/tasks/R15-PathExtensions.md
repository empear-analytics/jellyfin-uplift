# R15 - refactor `Emby.Server.Implementations/Library/PathExtensions.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **7.83** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 253 lines (103 lines of code) |
| Git churn | 41 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 86% / 84% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/PathExtensions.cs
```

The score must read 7.83. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetAttributeValue` | 20 | 27 | 6 | 4 | 3 |
| 2 | `TryReplaceSubPath` | 126 | 10 | - | - | 5 |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetAttributeValue` (line 20)

- **Recipe G (guard clauses)** - nesting is 6, target is under 4.
- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 27): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(PathExtensions): simplify GetAttributeValue`

### Step 2: `TryReplaceSubPath` (line 126)

- **Recipe P (named predicate)** - 5 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

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
Task: R15 (Emby.Server.Implementations/Library/PathExtensions.cs)
Baseline score: 7.83
Final score:    <x.xx>
Steps completed: <n> of 2
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
