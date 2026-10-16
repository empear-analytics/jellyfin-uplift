# R02 - refactor `MediaBrowser.Model/Dlna/StreamBuilder.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **1.65** / 10 (Red) |
| Exit target | **10.00** |
| File size | 2493 lines (1224 lines of code) |
| Git churn | 338 commits |
| Part | dlna (`MediaBrowser.Model/Dlna`) |
| Recipes needed | E, G, P, S |
| Smells without a recipe | Excess Number of Function Arguments, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 47% / 49% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

The score must read 1.65. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Brain Method | 3 | G, E, P |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Lines of Code in a Single File | 2 | S (last) |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Excess Number of Function Arguments | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |

This file also trips **Lines of Code in a Single File**. Splitting it is the *last* step.
Reduce method-level complexity first; the file may drop below the limit without a split.

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- The existing tests call these internal members directly: `ContainerReasons`. Keep each one, with
  its name, signature and class. You may move its body into new private methods.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetDefaultSubtitleStreamIndex` | 551 | 16 | 4 | 3 | 7 |
| 2 | `BuildVideoItem` | 648 | 51 | 4 | 5 | - |
| 3 | `GetExternalSubtitleProfile` | 1576 | 31 | - | 2 | 11 |
| 4 | `GetOptimalAudioStream` | 78 | 29 | - | 4 | 2 |
| 5 | `GetVideoDirectPlayProfile` | 1288 | 24 | - | 2 | 6 |
| 6 | `GetDefaultAudioBitrate` | 1155 | 16 | - | 2 | 3 |
| 7 | `ApplyTranscodingConditions` | 1755 | - | 4 | 27 | - |
| 8 | `BuildStreamVideoItem` | 934 | 53 | - | 5 | - |
| 9 | `GetSubtitleProfile` | 1465 | 28 | - | 2 | - |
| 10 | `GetAudioBitrate` | 1188 | 18 | - | - | 8 |
| 11 | `GetAudioDirectPlayProfile` | 437 | 17 | - | 3 | - |
| 12 | `GetVideoTranscodeProfile` | 847 | 16 | - | - | 2 |
| 13 | `NormalizeMediaSourceFormatIntoSingleContainer` | 408 | 10 | - | - | 2 |
| 14 | `GetTranscodeReasonForFailedCondition` | 307 | 28 | - | - | - |
| 15 | `GetTranscodeReasonsFromDirectPlayProfile` | 506 | 12 | - | - | - |
| 16 | `SortMediaSources` | 262 | 10 | - | - | - |
| 17 | `GetMaxAudioBitrateForTotalBitrate` | 1243 | 9 | - | - | - |
| 18 | `ValidateMediaOptions` | 1681 | 9 | - | - | - |
| 19 | `GetCompatibilityAudioCodec` | 2451 | - | - | - | - |
| 20 | `GetCompatibilityAudioCodecDirect` | 2481 | - | - | - | - |
| 21 | `GetProfileConditionsForAudio` | 1731 | - | - | - | - |
| 22 | `GetProfileConditionsForVideoAudio` | 1712 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetDefaultSubtitleStreamIndex` (line 551)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 7 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 16): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify GetDefaultSubtitleStreamIndex`

### Step 2: `BuildVideoItem` (line 648)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 51): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify BuildVideoItem`

### Step 3: `GetExternalSubtitleProfile` (line 1576)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 11 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 31): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetExternalSubtitleProfile`

### Step 4: `GetOptimalAudioStream` (line 78)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 29): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify GetOptimalAudioStream`

### Step 5: `GetVideoDirectPlayProfile` (line 1288)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 6 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 24): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetVideoDirectPlayProfile`

### Step 6: `GetDefaultAudioBitrate` (line 1155)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 16): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify GetDefaultAudioBitrate`

### Step 7: `ApplyTranscodingConditions` (line 1755)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 27 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity None): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify ApplyTranscodingConditions`

### Step 8: `BuildStreamVideoItem` (line 934)

- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 53): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (9 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify BuildStreamVideoItem`

### Step 9: `GetSubtitleProfile` (line 1465)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 28): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetSubtitleProfile`

### Step 10: `GetAudioBitrate` (line 1188)

- **Recipe P (named predicate)** - 8 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 18): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify GetAudioBitrate`

### Step 11: `GetAudioDirectPlayProfile` (line 437)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 17): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify GetAudioDirectPlayProfile`

### Step 12: `GetVideoTranscodeProfile` (line 847)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 16): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetVideoTranscodeProfile`

### Step 13: `NormalizeMediaSourceFormatIntoSingleContainer` (line 408)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamBuilder): simplify NormalizeMediaSourceFormatIntoSingleContainer`

### Step 14: `GetTranscodeReasonForFailedCondition` (line 307)

- **Recipes G, E, P** - cyclomatic complexity 28; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify GetTranscodeReasonForFailedCondition`

### Step 15: `GetTranscodeReasonsFromDirectPlayProfile` (line 506)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify GetTranscodeReasonsFromDirectPlayProfile`

### Step 16: `SortMediaSources` (line 262)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify SortMediaSources`

### Step 17: `GetMaxAudioBitrateForTotalBitrate` (line 1243)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify GetMaxAudioBitrateForTotalBitrate`

### Step 18: `ValidateMediaOptions` (line 1681)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamBuilder): simplify ValidateMediaOptions`

### Step 19: `GetCompatibilityAudioCodec` (line 2451)

- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetCompatibilityAudioCodec`

### Step 20: `GetCompatibilityAudioCodecDirect` (line 2481)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetCompatibilityAudioCodecDirect`

### Step 21: `GetProfileConditionsForAudio` (line 1731)

- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetProfileConditionsForAudio`

### Step 22: `GetProfileConditionsForVideoAudio` (line 1712)

- *Excess Number of Function Arguments* (9 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamBuilder): simplify GetProfileConditionsForVideoAudio`

### File-level smells

- *Lines of Code in a Single File*: S (last)
- *Overall Code Complexity*: follows the functions
- *Primitive Obsession*: no recipe in the playbook. Report it.

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
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamBuilder.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamBuilder.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamBuilder.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R02 (MediaBrowser.Model/Dlna/StreamBuilder.cs)
Baseline score: 1.65
Final score:    <x.xx>
Steps completed: <n> of 22
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
