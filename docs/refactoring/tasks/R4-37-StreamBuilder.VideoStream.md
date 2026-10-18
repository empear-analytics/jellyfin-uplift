# R4-37 - refactor `MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.26** / 10 (Green) |
| Exit target | **10.00** |
| File size | 151 lines (102 lines of code) |
| Git churn | 18 commits |
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
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs
```

The score must read 9.26. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
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
| 1 | `BuildStreamVideoItem` | 21 | 12 | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `BuildStreamVideoItem` (line 21)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (9 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamBuilder.VideoStream): simplify BuildStreamVideoItem`

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
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-37 (MediaBrowser.Model/Dlna/StreamBuilder.VideoStream.cs)
Baseline score: 9.26
Final score:    9.27
Steps completed: 1 of 1 (Recipe E rule 9 on BuildStreamVideoItem; one commit)
Steps reverted:  9 candidates measured alone, all FLAT, reverted (none committed):
                 framerate ternary (E rule 9), timestamp ternary, isSecondaryAudio ternary,
                 GlobalMaxAudioChannels ternary, directAudioStream ternary, video and audio
                 leading-run predicates (P), max-bitrate run (E rule 9), audio-limits run
                 (E rule 9). Also the all-seven extraction set on top of the base: WORSE,
                 Large Method revealed at LoC 75 once Complex Method went away.
Smells left without a recipe: Complex Method (cc 12, still flagged; LoC too high for Large
                 Method to clear with rules 9/P/G alone); Excess Number of Function Arguments
                 (9 args, signature frozen by the brief; rule 12 is outside this brief's rules)
Build: PASS (0 errors, 211 warnings, no new warning)
Tests: PASS (full solution: all projects pass; Model 808/808, Integration 429 passed, 3 skipped)
Commits: 7c99a65d2d
Unsure about: (1) The +0.01 gain comes from removing two code lines (LoC), not a smell:
                 the metric appears to respond to method size. (2) Pair F1+F2 (both E rule 9
                 runs in BuildStreamVideoItem) measured 9.27 -> 9.28 UP together, each alone
                 FLAT. NOT committed: the brief says keep only the best candidate measured
                 alone. Playbook rule 1 allows several applications of one recipe in one method,
                 so it can be approved as a second commit if the coordinator wants it.
                 (3) Recipe G does not apply: no nesting of depth 4 or more.
```

Candidate scores (base 9.26 unless noted): framerate 9.26 (cc 10), timestamp 9.26 (cc 11),
isSecondaryAudio 9.26 (cc 11), GlobalMaxAudioChannels 9.26 (cc 11), directAudioStream 9.26
(cc 11), video lead-run P 9.26 (cc 11), audio lead-run P 9.26 (cc 11), qualifier run 9.27 UP
(committed). On top of the commit: each candidate 9.27 FLAT, F1+F2 9.28 UP (not committed),
and all remaining singles together 9.28 WORSE (Large Method).
