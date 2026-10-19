# R4-50 - refactor `MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.53** / 10 (Green) |
| Exit target | **10.00** |
| File size | 457 lines (215 lines of code) |
| Git churn | 4 commits |
| Part | dlna (`MediaBrowser.Model/Dlna`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs
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
| 1 | `GetOptimalAudioStream` | 51 | 9 | - | 3 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetOptimalAudioStream` (line 51)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder.AudioStream): simplify GetOptimalAudioStream`

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
dotnet test tests/Jellyfin.Model.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-50 (MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs)
Baseline score: 9.53
Final score:    <x.xx>
Steps completed: <n> of 1
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```

## 10. Run report (h-R4-50b, re-run)

```
Task: R4-50 (MediaBrowser.Model/Dlna/StreamBuilder.AudioStream.cs)
Baseline score: 9.53
Final score:    9.84
Steps completed: 1 of 1 (Recipe E rule 7 tail with rule 12 context struct, 9.53 -> 9.84 UP)
Steps reverted:  1   (rule 9 single-value extraction of the GetCompatibilityAudioCodec call in the
                      DirectPlay block, measured alone: FLAT 9.84 -> 9.84)
Candidates not measured: Recipe G on the outer `if (directPlayMethod is PlayMethod.DirectPlay)`:
                      the body falls through into the DirectStream block, so inverting it needs a
                      merged condition or goto (both forbidden). Recipe P: no compound boolean left.
Smells left without a recipe: Bumpy Road Ahead (bumps 2) in GetOptimalAudioStream, lines 51-120.
                      The DirectPlay and DirectStream blocks both assign the local transcodeReasons
                      and return StreamInfo on some paths; a helper would need a tuple or out value.
Build: PASS (0 errors, 211 warnings, same as base; none in StreamBuilder.AudioStream.cs)
Tests: PASS (full solution, 17 test projects, 0 failed; Model 808/808;
       Integration 429 passed, 3 skipped)
Commits: b920e3ab35 refactor(StreamBuilder.AudioStream): simplify GetOptimalAudioStream (9.84 UP)
Unsure about: The 3 Integration and 17 Server.Implementations skips were not compared against
       the base. The rule 9 candidate was FLAT and is reverted, not committed.
```
