# R4-18 - refactor `Jellyfin.Api/Helpers/StreamingHelpers.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.37** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 813 lines (419 lines of code) |
| Git churn | 87 commits |
| Part | api (`Jellyfin.Api/Helpers`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 69% / 44% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/StreamingHelpers.cs
```

The score must read 8.37. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetCodecExtension` | 392 | 11 | - | 2 | - |
| 2 | `ApplyParam` | 526 | 35 | - | - | - |
| 3 | `GetOutputFilePath` | 487 | - | - | - | - |
| 4 | `GetStreamingState` | 46 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetCodecExtension` (line 392)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 11): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamingHelpers): simplify GetCodecExtension`

### Step 2: `ApplyParam` (line 526)

- **Recipes G, E, P** - cyclomatic complexity 35; reduce it with the recipes above as they apply.

Commit message: `refactor(StreamingHelpers): simplify ApplyParam`

### Step 3: `GetOutputFilePath` (line 487)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamingHelpers): simplify GetOutputFilePath`

### Step 4: `GetStreamingState` (line 46)

- *Excess Number of Function Arguments* (11 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(StreamingHelpers): simplify GetStreamingState`

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
dotnet test tests/Jellyfin.Api.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Jellyfin.Api/Helpers/StreamingHelpers.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/StreamingHelpers.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Helpers/StreamingHelpers.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Jellyfin.Api/Helpers/StreamingHelpers.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-18 (Jellyfin.Api/Helpers/StreamingHelpers.cs)
Baseline score: 8.77 (HEAD; the 8.37 in this file predates commit 0946d5c9e0 GetCodecExtension, as the brief says)
Final score:    8.77 (no step scored UP, so no code change was kept)
Steps completed: 0 of 4
Steps reverted:  every candidate, each measured alone with codehealth_check.py and reverted, all FLAT at 8.77:
  - ApplyParam (cc 35): not split. It is a void switch: its 34 sections end in `break`, not `return`, so rule 11 (suffix of a switch statement's sections that return or throw) does not apply. No other allowed recipe fits it (no run of statements, no if / else-if chain, no nesting, no compound condition, no return tail).
  - C1 GetOutputFilePath, rule 9, the filename run (data, MD5) to StreamingParamFilename: FLAT 8.77
  - C2 GetStreamingState, rule 9, the `ext` ternary to StreamingParamOutputExtension: FLAT 8.77
  - C3 GetPlaybackMediaSource, Recipe P, `mediaSource is null && Guid.Parse(...)` to StreamingParamUsesItemIdAsSourceId: FLAT 8.77
  - C4 ApplySupportedCodecs, Recipe P, `IsVideoRequest && !IsNullOrWhiteSpace(VideoCodec)` to StreamingParamHasVideoCodecRequest: FLAT 8.77
  - C5 ParseStreamOptions, Recipe P, `Key.Length > 0 && char.IsLower(Key[0])` to StreamingParamIsLowerCaseKey: FLAT 8.77
  Rule 13, rule 7 and Recipe G: no site in this file they apply to (no else-if chain, no return tail with a block to move, no nesting of depth 4).
Smells left without a recipe: NONE that the allowed set can reach. Left, not tried (rule 12 is outside this run's brief):
  - Excess Number of Function Arguments, GetStreamingState (11 arguments): needs Recipe E rule 12 (context struct).
  - Excess Number of Function Arguments, GetOutputFilePath (5 arguments): needs Recipe E rule 12; the signature is frozen.
  - Complex Method, ApplyParam (cc 35): a void-switch split is a candidate new recipe (see below). Not applied.
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, same as base)
Tests: PASS except one failure that passed on rerun (flake under load). Full run: Api 156 passed, Server.Integration 429 passed (3 skipped), Providers 512 of 513 passed, all other projects passed. Failed: Jellyfin.Providers.Tests.MediaInfo.AtlFlacRegressionTests.AtlParser_MalformedFlac_DoesNotHang (5 s). Run alone once, it passed (85 ms). Tree equals HEAD, so it is unrelated to this file.
Commits: this report commit only (no code commit)
Unsure about: (1) candidate new recipe: a void switch whose sections end in `break` (ApplyParam, 34 cases, each a single assignment or one Apply* call); no recipe covers it. (2) Candidates C1-C5 are on helpers and other methods, because ApplyParam itself has no allowed site; all FLAT. (3) The Providers flake above.
```
