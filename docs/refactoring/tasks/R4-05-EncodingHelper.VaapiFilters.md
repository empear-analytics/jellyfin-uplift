# R4-05 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **7.51** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 938 lines (480 lines of code) |
| Git churn | 27 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs
```

The score must read 7.51. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |
| Primitive Obsession | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetIntelVaapiFullVidFiltersPrefered` | 112 | 24 | - | 2 | - |
| 2 | `GetAmdVaapiFullVidFiltersPrefered` | 489 | 22 | - | 3 | - |
| 3 | `GetVaapiLimitedVidFiltersPrefered` | 752 | 18 | - | - | - |
| 4 | `GetVaapiVidFilterChain` | 41 | 12 | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetIntelVaapiFullVidFiltersPrefered` (line 112)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 24): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.VaapiFilters): simplify GetIntelVaapiFullVidFiltersPrefered`

### Step 2: `GetAmdVaapiFullVidFiltersPrefered` (line 489)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 22): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.VaapiFilters): simplify GetAmdVaapiFullVidFiltersPrefered`

### Step 3: `GetVaapiLimitedVidFiltersPrefered` (line 752)

- **Recipes G, E, P** - cyclomatic complexity 18; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VaapiFilters): simplify GetVaapiLimitedVidFiltersPrefered`

### Step 4: `GetVaapiVidFilterChain` (line 41)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VaapiFilters): simplify GetVaapiVidFilterChain`

### File-level smells

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
dotnet test tests/Jellyfin.Controller.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-05 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs)
Baseline score: 7.51 (task file); 7.82 at HEAD 90a884fe55 (round 5 start), measured and as the coordinator stated
Final score:    7.82 (no step kept)
Steps completed: 0 of 4 (the second-struct experiment on the decoder family; no step kept)
Steps reverted:  0 committed; 3 candidates measured alone and reverted, none kept
  - Amd decoder chain (`if (isSwDecoder)` ... `else if (isVaapiDecoder)`), second struct VaapiSecondAmdDecoderContext
    (10 members), helper AddVaapiSecondAmdDecoderFilters(context, decoder, mainFilters): 7.95 UP. Amd Bumpy Road gone,
    Amd cc 16 -> 12; the new helper is flagged for Bumpy Road (2 bumps). Best UP candidate, NOT kept: the helper also
    takes the first struct VaapiAmdFullContext (the software branch calls AddVaapiAmdSwScaleFilters(context, ...) and
    reads state, options, doVkTonemap, reqW..reqMaxH from it). Rule 12 says each struct serves one family.
  - Intel decoder chain, second struct VaapiSecondIntelDecoderContext (10 members), helper
    AddVaapiSecondIntelDecoderFilters(context, decoder, options, mainFilters), first struct built above the chain:
    7.90 UP. Intel cc 19 -> 15, Intel Bumpy Road stays at 2. Not kept, same reason (reads the first struct).
  - Amd vaapi branch alone as the tail of the chain (`else if (isVaapiDecoder)` moved to a void helper, struct
    VaapiSecondAmdDecoderContext with 12 members, state, options, mainFilters plain): 7.86 UP. Amd cc 16 -> 14, Amd
    Bumpy Road 3 -> 2. Not kept: a void tail is not a rule 13 form (rule 13 covers only assign and return tails).
  - No exact candidate exists. Cap: 12 struct members + 3 plain parameters = 15 values.
    Intel decoder chain needs 16 values: guards isSwDecoder and isVaapiDecoder, doTonemap, doOclTonemap,
    doDeintH2645, isMjpegEncoder, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH,
    transposeDir, doVaVppTranspose. The Intel software block is the head of the else-if chain, and rule 13 moves
    only the tail, so it cannot move alone. The Intel vaapi tail alone needs 13 with its guard.
    Amd decoder chain needs 15 values plus the first struct, which its software branch passes on.
    Amd vaapi tail alone fits exactly (12 members, 4 parameters), but needs the void tail of candidate 3.
Smells left without a recipe: Primitive Obsession (file level; no recipe).
Smells left with recipe-level candidates only:
  - Bumpy Road Ahead: GetAmdVaapiFullVidFiltersPrefered (3 bumps), GetIntelVaapiFullVidFiltersPrefered (2 bumps).
    Tried via rule 12: the three candidates above, none kept.
  - Complex Method: GetIntelVaapiFullVidFiltersPrefered cc 19 (task file says 16), GetAmdVaapiFullVidFiltersPrefered
    cc 16, GetVaapiLimitedVidFiltersPrefered cc 14, GetVaapiVidFilterChain cc 10 (task file says 12). Not measured
    beyond the struct candidates, which were the scope of this run.
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, the base count)
Tests: PASS apart from one flake. dotnet test Jellyfin.sln --no-build: 5943 passed, 21 skipped, 1 failed.
  The failure is Jellyfin.Providers.Tests.MediaInfo.AtlFlacRegressionTests.AtlParser_MalformedFlac_DoesNotHang
  ("Test execution timed out after 5000 milliseconds" under full-suite load, on unchanged HEAD). Run alone it passes
  (1 of 1, 80 ms). Flake; not caused by this run; not looped.
Commits: no code commit; report commit only (docs/refactoring/tasks/R4-05 file, see git log)
Unsure about:
  - Whether rule 12 allows a decoder block to read the first struct. Candidates 1 and 2 (7.95, 7.90) depend on it.
    The coordinator should rule on it before either is taken. Candidate patches are not in any commit; they are
    described above and can be re-derived from it.
  - Whether a void else-if tail should be a recipe (candidate 3, 7.86).
  - The task file baseline (7.51, Intel cc 16, Vaapi chain cc 12) is stale. HEAD 90a884fe55 measures 7.82 with
    Intel cc 19.
```

### Round 6 (void tail, Haiku 5.5)

```
Task: R4-05 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.VaapiFilters.cs), void tail candidate
Baseline score: 7.82 (HEAD 9a5aa80194, measured)
Final score:    7.86 (commit a462f3a2f3)
Steps completed: 1 (commit a462f3a2f3: Recipe E rule 13, void tail, with rule 12 struct)
  - else if (isVaapiDecoder) of GetAmdVaapiFullVidFiltersPrefered moved to private void
    VaapiVoidAmdDecoderFilters(context, state, options, mainFilters); original gets else { ... }.
    Struct VaapiVoidAmdDecoderContext, 12 members (IsVaapiDecoder, DoDeintH2645, InW, InH, ReqW,
    ReqH, ReqMaxW, ReqMaxH, DoVkTranspose, DoVkTonemap, HasSubs, IsMjpegEncoder); helper has 4
    parameters. transposeDir is not read by the branch, so it is not a member.
    Measured alone: 7.86 UP (Amd cc 16 -> 14, Amd Bumpy Road 3 -> 2; new helper not flagged).
Candidates measured: only the one above. No other candidate measured in this run.
Smells left:
  - Bumpy Road Ahead: GetAmdVaapiFullVidFiltersPrefered (2 bumps, lines 515-623),
    GetIntelVaapiFullVidFiltersPrefered (2 bumps). Not attempted in this run.
  - Complex Method: GetIntelVaapiFullVidFiltersPrefered cc 19, GetVaapiLimitedVidFiltersPrefered cc 14,
    GetAmdVaapiFullVidFiltersPrefered cc 14, GetVaapiVidFilterChain cc 10. Not attempted in this run.
  - Primitive Obsession (file level; no recipe).
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, the base count)
Tests: Controller 1411 passed; Integration 429 passed, 3 skipped; full solution (dotnet test Jellyfin.sln
  --no-build) 0 failed, 21 skipped.
Guard: PASS (110 literals unchanged)
Unsure about: nothing for this step. The round-5 open question (whether a void else-if tail is a recipe)
  is answered by rule 13's void form in the playbook as of 2026-10-19.
```
