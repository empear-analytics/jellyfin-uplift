# R05 - refactor `Jellyfin.Api/Controllers/DynamicHlsController.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **2.90** / 10 (Red) |
| Exit target | **10.00** |
| File size | 2097 lines (1262 lines of code) |
| Git churn | 162 commits |
| Part | api (`Jellyfin.Api/Controllers`) |
| Recipes needed | D, E, G, P, S |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 58% / 48% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Controllers/DynamicHlsController.cs
```

The score must read 2.90. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Lines of Code in a Single File | 2 | S (last) |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Code Duplication | 2 | D |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |

This file also trips **Lines of Code in a Single File**. Splitting it is the *last* step.
Reduce method-level complexity first; the file may drop below the limit without a split.

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- The existing tests call these internal members directly: `GetDolbyVisionHevcCodecTag`, `GetSegmentLengthsInternal`, `WaitForActiveTranscodingRequests`. Keep each one, with
  its name, signature and class. You may move its body into new private methods.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetAudioArguments` | 1664 | 31 | - | 3 | 11 |
| 2 | `GetVideoArguments` | 1798 | 26 | - | 4 | 3 |
| 3 | `GetSegmentResult` | 1924 | 14 | 4 | 3 | - |
| 4 | `GetHlsAudioSegment` | 1271 | 13 | - | - | - |
| 5 | `GetMasterHlsAudioPlaylist` | 579 | 13 | - | - | - |
| 6 | `GetVariantHlsAudioPlaylist` | 915 | 13 | - | - | - |
| 7 | `GetHlsVideoSegment` | 1089 | 12 | - | - | - |
| 8 | `GetMasterHlsVideoPlaylist` | 406 | 12 | - | - | - |
| 9 | `GetVariantHlsVideoPlaylist` | 746 | 12 | - | - | - |
| 10 | `GetDynamicSegment` | 1427 | 10 | - | 2 | - |
| 11 | `GetLiveHlsStream` | 165 | 18 | - | - | - |
| 12 | `GetCommandLineArguments` | 1578 | 12 | - | - | - |
| 13 | `DynamicHlsController` | 78 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetAudioArguments` (line 1664)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 11 complex conditional expression(s).

Commit message: `refactor(DynamicHlsController): simplify GetAudioArguments`

### Step 2: `GetVideoArguments` (line 1798)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(DynamicHlsController): simplify GetVideoArguments`

### Step 3: `GetSegmentResult` (line 1924)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetSegmentResult`

### Step 4: `GetHlsAudioSegment` (line 1271)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (52 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetHlsAudioSegment`

### Step 5: `GetMasterHlsAudioPlaylist` (line 579)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (48 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetMasterHlsAudioPlaylist`

### Step 6: `GetVariantHlsAudioPlaylist` (line 915)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (47 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetVariantHlsAudioPlaylist`

### Step 7: `GetHlsVideoSegment` (line 1089)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (54 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetHlsVideoSegment`

### Step 8: `GetMasterHlsVideoPlaylist` (line 406)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (51 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetMasterHlsVideoPlaylist`

### Step 9: `GetVariantHlsVideoPlaylist` (line 746)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (49 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetVariantHlsVideoPlaylist`

### Step 10: `GetDynamicSegment` (line 1427)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(DynamicHlsController): simplify GetDynamicSegment`

### Step 11: `GetLiveHlsStream` (line 165)

- **Recipes G, E, P** - cyclomatic complexity 18; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (51 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify GetLiveHlsStream`

### Step 12: `GetCommandLineArguments` (line 1578)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(DynamicHlsController): simplify GetCommandLineArguments`

### Step 13: `DynamicHlsController` (line 78)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify DynamicHlsController`

### File-level smells

- *Lines of Code in a Single File*: S (last)
- *Overall Code Complexity*: follows the functions
- *Primitive Obsession*: no recipe in the playbook. Report it.

### Final step: re-measure, then stop

Once the score reaches 10.00, **stop**.

## 7. Verification - run all four after EVERY step

**1 - it still builds, warnings included.**

```bash
dotnet build Jellyfin.sln
```

Zero errors, and no new warning in the file you changed.

**2 - the tests that cover this part still pass.**

```bash
dotnet test tests/Jellyfin.Api.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Jellyfin.Api/Controllers/DynamicHlsController.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Jellyfin.Api/Controllers/DynamicHlsController.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Controllers/DynamicHlsController.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Jellyfin.Api/Controllers/DynamicHlsController.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R05 (Jellyfin.Api/Controllers/DynamicHlsController.cs)
Baseline score: 2.90
Final score:    <x.xx>
Steps completed: <n> of 13
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
