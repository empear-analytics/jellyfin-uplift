# R04 - refactor `MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **2.18** / 10 (Red) |
| Exit target | **10.00** |
| File size | 1796 lines (847 lines of code) |
| Git churn | 239 commits |
| Part | probing (`MediaBrowser.MediaEncoding/Probing`) |
| Recipes needed | E, G, P, S |
| Smells without a recipe | Excess Number of Function Arguments, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 63% / 68% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs
```

The score must read 2.18. If it does not, this file changed after the task was
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

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- The existing tests call these internal members directly: `GetEstimatedAudioBitrate`, `GetFrameRate`, `IsNearSquarePixelSar`. Keep each one, with
  its name, signature and class. You may move its body into new private methods.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetMediaStream` | 700 | 77 | 4 | 12 | 11 |
| 2 | `GetMediaInfo` | 98 | - | 4 | 6 | 2 |
| 3 | `FetchWtvInfo` | 1619 | 22 | - | 2 | 2 |
| 4 | `NormalizeFormat` | 294 | 12 | - | 2 | 4 |
| 5 | `SetAudioInfoFromTags` | 1323 | 36 | - | 9 | - |
| 6 | `GetAspectRatio` | 1102 | 18 | - | - | 4 |
| 7 | `GetEstimatedAudioBitrate` | 341 | 14 | - | - | 2 |
| 8 | `ReadFromDictNode` | 432 | 13 | 4 | - | - |
| 9 | `FetchFromItunesInfo` | 369 | 10 | 4 | - | - |
| 10 | `ReadValueArray` | 496 | 9 | 4 | - | - |
| 11 | `GetNameValuePair` | 591 | 9 | - | - | - |
| 12 | `ExtractTimestamp` | 1743 | - | - | - | 2 |
| 13 | `GetMpegTimestamp` | 1782 | - | - | - | 3 |
| 14 | `IsNearSquarePixelSar` | 1193 | - | - | - | 3 |
| 15 | `ProcessPairs` | 542 | - | - | 3 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetMediaStream` (line 700)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 12 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 11 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 77): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify GetMediaStream`

### Step 2: `GetMediaInfo` (line 98)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity None): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(ProbeResultNormalizer): simplify GetMediaInfo`

### Step 3: `FetchWtvInfo` (line 1619)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 22): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify FetchWtvInfo`

### Step 4: `NormalizeFormat` (line 294)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify NormalizeFormat`

### Step 5: `SetAudioInfoFromTags` (line 1323)

- **Recipe E (extract function)** - 9 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 36): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify SetAudioInfoFromTags`

### Step 6: `GetAspectRatio` (line 1102)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 18): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify GetAspectRatio`

### Step 7: `GetEstimatedAudioBitrate` (line 341)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify GetEstimatedAudioBitrate`

### Step 8: `ReadFromDictNode` (line 432)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Then, while Complex Method remains** (cyclomatic complexity 13): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify ReadFromDictNode`

### Step 9: `FetchFromItunesInfo` (line 369)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify FetchFromItunesInfo`

### Step 10: `ReadValueArray` (line 496)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(ProbeResultNormalizer): simplify ReadValueArray`

### Step 11: `GetNameValuePair` (line 591)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(ProbeResultNormalizer): simplify GetNameValuePair`

### Step 12: `ExtractTimestamp` (line 1743)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(ProbeResultNormalizer): simplify ExtractTimestamp`

### Step 13: `GetMpegTimestamp` (line 1782)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(ProbeResultNormalizer): simplify GetMpegTimestamp`

### Step 14: `IsNearSquarePixelSar` (line 1193)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(ProbeResultNormalizer): simplify IsNearSquarePixelSar`

### Step 15: `ProcessPairs` (line 542)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(ProbeResultNormalizer): simplify ProcessPairs`

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
dotnet test tests/Jellyfin.MediaEncoding.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R04 (MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs)
Baseline score: 2.18
Final score:    <x.xx>
Steps completed: <n> of 15
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
