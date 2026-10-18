# R4-04 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **7.32** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1388 lines (654 lines of code) |
| Git churn | 696 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Constructor Over-Injection, Low Cohesion |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 53% / 54% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

The score must read 7.32. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Low Cohesion | 3 | none - report it |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Constructor Over-Injection | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `TryStreamCopy` | 825 | 9 | - | - | 2 |
| 2 | `GetInputModifier` | 916 | 9 | - | - | - |
| 3 | `EncodingHelper` | 162 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `TryStreamCopy` (line 825)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify TryStreamCopy`

### Step 2: `GetInputModifier` (line 916)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetInputModifier`

### Step 3: `EncodingHelper` (line 162)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify EncodingHelper`

### File-level smells

- *Low Cohesion*: no recipe in the playbook. Report it.
- *Overall Code Complexity*: follows the functions

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-04 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs)
Baseline score: 7.32
Final score:    <x.xx>
Steps completed: <n> of 3
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
