# R4-43 - refactor `Emby.Server.Implementations/Library/LibraryManager.Changes.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.38** / 10 (Green) |
| Exit target | **10.00** |
| File size | 372 lines (193 lines of code) |
| Git churn | 1 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | D |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Changes.cs
```

The score must read 9.38. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Code Duplication | 2 | D |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `NotifyItemsAdded` | 190 | - | - | - | - |
| 2 | `NotifyItemsUpdated` | 260 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `NotifyItemsAdded` (line 190)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by one or two values (see the recipe).

Commit message: `refactor(LibraryManager.Changes): simplify NotifyItemsAdded`

### Step 2: `NotifyItemsUpdated` (line 260)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by one or two values (see the recipe).

Commit message: `refactor(LibraryManager.Changes): simplify NotifyItemsUpdated`

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
python tools/refactor_guard.py Emby.Server.Implementations/Library/LibraryManager.Changes.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Changes.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/LibraryManager.Changes.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --dedup --together Emby.Server.Implementations/Library/LibraryManager.Changes.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-43 (Emby.Server.Implementations/Library/LibraryManager.Changes.cs)
Baseline score: 9.38
Final score:    10.00
Steps completed: 1 of 2 (step 1 committed; step 2 not needed, score already 10.00)
Steps reverted:  1 (the step-1 candidate, see below: build failed with CS8602, fixed by the
                 nullable case 2 parameter, then re-measured UP; the committed version is the fixed one)
                 Measured and reverted alone: predicate IsLibraryItem in NotifyItemsAdded (FLAT, 9.38)
Smells left without a recipe: NONE reported by CodeScene. Recipe D as a merge of the two copies
                 is not allowed (four differing places, one a log template); see Unsure about.
Build: PASS (dotnet build Jellyfin.sln --no-incremental, 0 errors, 211 warnings = base)
Tests: PASS (Server.Implementations 1102 passed, 17 skipped; Integration 429 passed, 3 skipped;
       full Jellyfin.sln --no-build: all 17 test projects pass, 0 failures)
Commits: d64bd98a6f refactor(LibraryManager.Changes): simplify NotifyItemsAdded (9.38 -> 10.00, UP)
Unsure about: (1) The 10.00 comes from moving the try/catch of NotifyItemsAdded into
                 ChangesRaiseItemAdded (Recipe E rule 9). The two notify methods still have the same
                 shape, and the copy in NotifyItemsUpdated is unchanged. CodeScene no longer reports
                 Code Duplication. The UpdateItemsAsync copy was measured alone as well (also 10.00, UP,
                 not committed, not built): the tie went to task order.
             (2) Recipe D was not measured: the copies differ in the event (ItemAdded/ItemUpdated), the
                 Parent expression, UpdateReason, and the log template (CA2254). The playbook allows at
                 most two differing values, so no Recipe D step is legal here.
             (3) The new helper takes the checked event as a parameter (nullable flow case 2). The
                 event is read once per item, before the event arguments are built, as before.
             (4) New method names start with Changes, per the task instructions.
```
