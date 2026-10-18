# R4-01 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **5.15** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1166 lines (533 lines of code) |
| Git churn | 31 commits |
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
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs
```

The score must read 5.15. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Brain Method | 3 | G, E, P |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |
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
| 1 | `GetHwaccelType` | 276 | - | 4 | 5 | - |
| 2 | `ResolveHwDecoderByType` | 64 | 16 | - | 2 | - |
| 3 | `GetQsvHwVidDecoder` | 567 | 12 | - | 3 | - |
| 4 | `ResolveNvdecAccelType` | 710 | 12 | - | 3 | - |
| 5 | `ResolveVaapiAccelType` | 870 | 11 | - | 3 | - |
| 6 | `ResolveVideotoolboxAccelType` | 960 | 9 | - | 3 | - |
| 7 | `ResolveRkmppAccelType` | 1042 | 14 | - | - | - |
| 8 | `ResolveAmfAccelType` | 800 | 9 | - | - | - |
| 9 | `GetHwDecoderName` | 178 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetHwaccelType` (line 276)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity None): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify GetHwaccelType`

### Step 2: `ResolveHwDecoderByType` (line 64)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 16): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveHwDecoderByType`

### Step 3: `GetQsvHwVidDecoder` (line 567)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify GetQsvHwVidDecoder`

### Step 4: `ResolveNvdecAccelType` (line 710)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveNvdecAccelType`

### Step 5: `ResolveVaapiAccelType` (line 870)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 11): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveVaapiAccelType`

### Step 6: `ResolveVideotoolboxAccelType` (line 960)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveVideotoolboxAccelType`

### Step 7: `ResolveRkmppAccelType` (line 1042)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveRkmppAccelType`

### Step 8: `ResolveAmfAccelType` (line 800)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify ResolveAmfAccelType`

### Step 9: `GetHwDecoderName` (line 178)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(EncodingHelper.HwDecoders): simplify GetHwDecoderName`

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-01 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.HwDecoders.cs)
Baseline score: 5.15
Final score:    <x.xx>
Steps completed: <n> of 9
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
