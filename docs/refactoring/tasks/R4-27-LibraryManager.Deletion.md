# R4-27 - refactor `Emby.Server.Implementations/Library/LibraryManager.Deletion.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.09** / 10 (Green) |
| Exit target | **10.00** |
| File size | 534 lines (295 lines of code) |
| Git churn | 3 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Deletion.cs
```

The score must read 9.09. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetInternalItemsPath` | 474 | 9 | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetInternalItemsPath` (line 474)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager.Deletion): simplify GetInternalItemsPath`

### File-level smells

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
dotnet test tests/Jellyfin.Server.Implementations.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Emby.Server.Implementations/Library/LibraryManager.Deletion.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Deletion.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/LibraryManager.Deletion.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Emby.Server.Implementations/Library/LibraryManager.Deletion.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-27 (Emby.Server.Implementations/Library/LibraryManager.Deletion.cs)
Baseline score: 9.09
Final score:    9.09 (no refactor committed)
Steps completed: 0 of 1
Steps reverted:  0 committed; candidates measured and reverted, none committed:
  - Recipe E rule 9 (wrapper around the single return statement, DeletionInternalItemsPathFor): 9.09 FLAT
  - Recipe E rule 11 (suffix of the switch arms, DeletionInternalItemsPathTail), outside the rules named in the brief: 9.38 UP at split points of 1, 2, 3 and 4 arms; not committed
  - Recipe G and Recipe P: no applicable site (no nested condition, no compound && or ||)
  - Recipe E rule 13: no else-if chain
Smells left without a recipe: NONE in the brief's recipe set. Overall Code Complexity (indication 2) remains; the only applicable recipe is rule 11, which the brief did not name.
Build: PASS (0 errors, 211 warnings, dotnet build Jellyfin.sln --no-incremental)
Tests: PASS (full suite on unchanged HEAD). One failure: Jellyfin.Providers.Tests.MediaInfo.AtlFlacRegressionTests.AtlParser_MalformedFlac_DoesNotHang (5 s under load); passed when run alone, so a flake.
Commits: none for the refactor; this report commit only
Unsure about: whether rule 11 may be used here. It is the one candidate that scores UP (9.38), and it is not among the rules the brief named. If the coordinator adds it, one commit at any split point should do. Recipe D and the file-level smell are untouched.
```
