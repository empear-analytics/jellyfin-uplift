# R4-08 - refactor `MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **7.86** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 346 lines (155 lines of code) |
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
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs
```

The score must read 7.86. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetSubtitleProfile` | 111 | 24 | - | 2 | - |
| 2 | `GetExternalSubtitleProfile` | 237 | 17 | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetSubtitleProfile` (line 111)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 24): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (7 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder.Subtitles): simplify GetSubtitleProfile`

### Step 2: `GetExternalSubtitleProfile` (line 237)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 17): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (6 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder.Subtitles): simplify GetExternalSubtitleProfile`

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
dotnet test tests/Jellyfin.Model.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-08 (MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs)
Baseline score: 7.86
Final score:    <x.xx>
Steps completed: <n> of 2
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```

## 10. Run report (h-R4-08c, re-run)

```
Task: R4-08 (MediaBrowser.Model/Dlna/StreamBuilder.Subtitles.cs)
Baseline score: 8.84 at HEAD (three earlier commits were already in; 7.86 above is the pre-campaign value)
Final score:    9.24
Steps completed: 1 of 2 (GetSubtitleProfile done; no UP candidate for GetExternalSubtitleProfile)
Steps reverted:  none committed; measured candidates that were FLAT were reverted (see below)
Candidates measured (codehealth_check.py, each alone):
  On HEAD at 8.84:
    GetSubtitleProfile, convertible embed loop (value or none)   9.24 UP  committed (2f3c8404ab)
    GetSubtitleProfile, both embed loops                         9.24 UP  same score, larger step, not taken
    GetSubtitleProfile, same-format embed loop (value or none)   9.09 UP  not taken (loop 2 scored higher)
    GetExternalSubtitleProfile, rule 10 on both guards           8.89 UP  not taken (9.24 below)
    GetExternalSubtitleProfile, rule 10 on allowConversion guard 8.84 FLAT
    GetExternalSubtitleProfile, rule 10 on IsInfiniteStream guard 8.84 FLAT
  On HEAD at 9.24 (after 2f3c8404ab):
    GetExternalSubtitleProfile, rule 10 on both guards           9.24 FLAT (cc 11 to 9, bumps still 2)
    GetSubtitleProfile, same-format embed loop (value or none)   9.24 FLAT
    GetExternalSubtitleProfile, requiresConversion check (value or none, rule 9) 9.24 FLAT
Smells left without a recipe:
  - GetExternalSubtitleProfile: Bumpy Road Ahead (2 bumps), both inside the if (IsMatchingExternalOrHlsProfile)
    block, which returns profile and also continues. Rule 10 forbids a return; rule 9 covers only a single value or none.
  - GetExternalSubtitleProfile: Complex Method cc 11 (cc 9 after the rule 10 guard step, still flagged; that step is FLAT).
  - Excess Number of Function Arguments: GetSubtitleProfile 7, GetExternalSubtitleProfile 6. Blocked by the frozen signatures.
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, none in StreamBuilder.Subtitles.cs)
Tests: PASS (full solution, 17 test projects, 0 failed; Model 808/808; Integration 429 passed, 3 skipped)
Guard: PASS (6 literals unchanged)
Commits: 2f3c8404ab refactor(StreamBuilder.Subtitles): simplify GetSubtitleProfile (8.84 to 9.24 UP)
Unsure about: The committed step is a value-or-none extraction. The loop returns the profile or none, the helper returns
       SubtitleProfile? (null for none), and the caller does if (x is not null) return x;. I read this as Recipe E rule 9
       ("produces a single value or none") with step 3, but the playbook does not state this call-site form; rule 7 gives a
       return only for a tail. The file already returns null as none in GetExternalSubtitleProfile. Please rule on it.
       Not measured, not in the catalogue: the if (IsMatchingExternalOrHlsProfile) block of GetExternalSubtitleProfile could
       become value or none if its continue is read as none; it needs five values (a rule 12 struct) and the playbook has no
       entry for continue becoming return null.
```
