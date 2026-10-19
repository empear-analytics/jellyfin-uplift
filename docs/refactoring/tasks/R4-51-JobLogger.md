# R4-51 - refactor `MediaBrowser.Controller/MediaEncoding/JobLogger.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.53** / 10 (Green) |
| Exit target | **10.00** |
| File size | 147 lines (73 lines of code) |
| Git churn | 39 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 75% / 78% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/JobLogger.cs
```

The score must read 9.53. If it does not, this file changed after the task was
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
| 1 | `ParseLogLine` | 62 | 10 | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `ParseLogLine` (line 62)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(JobLogger): simplify ParseLogLine`

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/JobLogger.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/JobLogger.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/JobLogger.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/JobLogger.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-51 (MediaBrowser.Controller/MediaEncoding/JobLogger.cs)
Baseline score: 9.53
Final score:    9.84
Steps completed: 1 of 1 (one commit, two steps of two recipes: rule 1 pair)
Steps reverted:  0 committed. Candidates measured alone on HEAD, both FLAT (9.53 -> 9.53), so
                 not committed alone: Recipe P on the progress condition (ParseLogLine cc 9);
                 Recipe E rule 13 on the bitrate else-if tail (ParseLogLine cc 9).
                 Together: 9.84 UP, all checks pass.
Smells left without a recipe: Bumpy Road Ahead (2 bumps) in ParseLogLine, not attempted. Neither
                 bump is covered by the playbook: the fps= branch is not a tail of the else-if chain
                 (rule 13), and the time= branch writes two outer variables (percent and
                 transcodingPosition, Recipe E rule 4). Complex Method is gone (cc 10 to 9).
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, the base count)
Tests: PASS (full solution, dotnet test Jellyfin.sln --no-build: 0 failed; Controller 1411,
       MediaEncoding 280 (1 skipped), Server.Integration 429 (3 skipped); no failing test)
Commits: 430e29488f refactor(JobLogger): simplify ParseLogLine (9.53 -> 9.84, UP)
Unsure about: (1) Rule 13 applied to a single-branch tail (only the bitrate branch moves, as
                 the task specified); the else branch is reached for every part the earlier
                 branches did not match, and the helper returns bitRate unchanged when the prefix
                 is absent, so behaviour is the same. (2) The task says one commit; this report is
                 filled in a separate docs commit, as earlier rounds did. (3) Harness reported
                 JobLogger.cs changed on disk after my own git checkout revert; git status was clean
                 and the file matched HEAD, so I took no further action.
```
