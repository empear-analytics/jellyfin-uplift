# R4-17 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.24** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 597 lines (349 lines of code) |
| Git churn | 16 commits |
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
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs
```

The score must read 8.24. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Overall Code Complexity | 2 | follows the functions |
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
| 1 | `GetVideoProcessingFilterParam` | 448 | 14 | - | - | - |
| 2 | `GetSwVidFilterChain` | 309 | 10 | - | - | - |
| 3 | `GetLibplaceboFilter` | 210 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetVideoProcessingFilterParam` (line 448)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoFilters): simplify GetVideoProcessingFilterParam`

### Step 2: `GetSwVidFilterChain` (line 309)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper.VideoFilters): simplify GetSwVidFilterChain`

### Step 3: `GetLibplaceboFilter` (line 210)

- *Excess Number of Function Arguments* (10 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(EncodingHelper.VideoFilters): simplify GetLibplaceboFilter`

### File-level smells

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
dotnet test tests/Jellyfin.Controller.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-17 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.VideoFilters.cs)
Baseline score: 8.54 (the file's score at the start of round 5 HEAD; the 8.24 above is stale)
Final score:    8.81
Steps completed: 1 of 3 (step 2, GetSwVidFilterChain, as named in this run's brief; steps 1 and 3 not run)
Steps reverted:  1 candidate in step 2 (replace the if/else tonemap-or-format block with a helper taking
                 a SwFilterChainContext struct: FLAT 8.81 -> 8.81, cc still 8, no gain). Also measured alone,
                 both FLAT: the deinterlace block extraction (cc 10 -> 9) and the software decoder check (cc 10 -> 9)
Smells left without a recipe: Primitive Obsession (file); Overall Code Complexity (file);
                 Excess Number of Function Arguments on GetLibplaceboFilter (step 3, not run in this run)
Build: PASS (dotnet build Jellyfin.sln --no-incremental, 0 errors, 211 warnings)
Tests: PASS (full solution: Controller 1411, Integration 429 + 3 skipped, all projects 0 failed)
Commits: 36eb952e05 refactor(EncodingHelper.VideoFilters): simplify GetSwVidFilterChain (8.54 -> 8.81)
Unsure about: The two extractions are both Recipe E (rule 9 with the guard moved, and rule 9 with a single
                 value) in one method; each alone FLAT, together UP. Rule 1 allows this, but the brief says to keep
                 only the best UP candidate. Blocked: the else-if chain tail (if (hasTextSubs) ... else if
                 (hasGraphicalSubs)) only mutates lists and assigns or returns nothing, so rule 13 does not match
                 its shape. Not measured. Recipe G and P: no match in the body.
```
