# R4-65 - refactor `Emby.Server.Implementations/Library/LibraryManager.Intros.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.84** / 10 (Green) |
| Exit target | **10.00** |
| File size | 166 lines (96 lines of code) |
| Git churn | 1 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Intros.cs
```

The score must read 9.84. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `ResolveIntro` | 113 | - | - | 3 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `ResolveIntro` (line 113)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager.Intros): simplify ResolveIntro`

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
python tools/refactor_guard.py Emby.Server.Implementations/Library/LibraryManager.Intros.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.Intros.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/LibraryManager.Intros.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Emby.Server.Implementations/Library/LibraryManager.Intros.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-65 (Emby.Server.Implementations/Library/LibraryManager.Intros.cs)
Baseline score: 9.84
Final score:    10.00
Steps completed: 1 of 1
Steps reverted:  0. Candidates measured alone, none committed (all FLAT except the one kept):
                 rule 13 else-if tail to IntrosResolveFromPath 9.84 (FLAT, bumps 3 to 2 in ResolveIntro, 2 in the helper);
                 rule 9 id block to IntrosResolveById 9.84 (FLAT); rule 9 else log to IntrosLogMissingPathAndId 9.84 (FLAT);
                 recipe P has-path to IntrosHasPath 9.84 (FLAT); rule 9 path try body to IntrosResolvePathVideo 10.00 (UP, kept).
Smells left without a recipe: NONE
Build: PASS (dotnet build Jellyfin.sln --no-incremental, 0 errors, 211 warnings as on the base)
Tests: PASS (Server.Implementations 1102 passed, Integration 429 passed; full solution 5944 passed, 21 skipped, 0 failed)
Commits: 0184d70ef3 (refactor(LibraryManager.Intros): simplify ResolveIntro, 9.84 to 10.00); this report commit follows it
Unsure about: The moved try body contains `return null;`. In IntrosResolvePathVideo it becomes `return null;` from the helper, and ResolveIntro stores that in `video` and falls through to its final `return video;`. That is the same value as before (the try body is the last thing before that return), checked by hand, not by a random-input comparison. Rule 9 does not state this shape explicitly; if the coordinator reads it as outside rule 9, this step is the one to reject.
```
