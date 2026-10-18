# R4-03 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **6.85** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1125 lines (532 lines of code) |
| Git churn | 23 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Low Cohesion, Primitive Obsession, String Heavy Function Arguments |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs
```

The score must read 6.85. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Low Cohesion | 3 | none - report it |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |
| Primitive Obsession | 2 | none - report it |
| String Heavy Function Arguments | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetEncoderParam` | 162 | 9 | - | - | - |
| 2 | `AppendVaapiCompressionLevel` | 265 | 13 | - | - | - |
| 3 | `AppendSvtAv1Preset` | 243 | 12 | - | - | - |
| 4 | `AppendLevelParam` | 976 | 9 | - | - | - |
| 5 | `AppendNvencPreset` | 304 | 9 | - | - | - |
| 6 | `GetHlsVideoKeyFrameArguments` | 517 | - | - | - | - |
| 7 | `NormalizeTranscodingLevel` | 431 | - | - | 3 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetEncoderParam` (line 162)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify GetEncoderParam`

### Step 2: `AppendVaapiCompressionLevel` (line 265)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify AppendVaapiCompressionLevel`

### Step 3: `AppendSvtAv1Preset` (line 243)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify AppendSvtAv1Preset`

### Step 4: `AppendLevelParam` (line 976)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify AppendLevelParam`

### Step 5: `AppendNvencPreset` (line 304)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify AppendNvencPreset`

### Step 6: `GetHlsVideoKeyFrameArguments` (line 517)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify GetHlsVideoKeyFrameArguments`

### Step 7: `NormalizeTranscodingLevel` (line 431)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper.VideoEncoderParams): simplify NormalizeTranscodingLevel`

### File-level smells

- *Low Cohesion*: no recipe in the playbook. Report it.
- *Primitive Obsession*: no recipe in the playbook. Report it.
- *String Heavy Function Arguments*: no recipe in the playbook. Report it.

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-03 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoEncoderParams.cs)
Baseline score: 6.85
Final score:    <x.xx>
Steps completed: <n> of 7
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
