# R4-35 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.25** / 10 (Green) |
| Exit target | **10.00** |
| File size | 220 lines (131 lines of code) |
| Git churn | 11 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs
```

The score must read 9.25. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetNvidiaVidFiltersPrefered` | 72 | 20 | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetNvidiaVidFiltersPrefered` (line 72)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 20): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper.NvidiaFilters): simplify GetNvidiaVidFiltersPrefered`

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
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-35 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.NvidiaFilters.cs)
Baseline score: 9.47 at HEAD 5338b1c343 (the task file says 9.25; the file was already at 9.47 after the earlier subtitle commit, per the round brief)
Final score:    9.47 (no code commit)
Steps completed: 0 of 1
Steps reverted:  2. Second context struct (Recipe E rule 12), each candidate measured alone:
                 (a) sw-decoder block -> NvidiaSecondAddSwDecoderFilters, struct NvidiaSecondSwDecoderContext (12 members): 9.47 -> 9.44 DOWN;
                 (b) nv-decoder block -> NvidiaSecondAddNvDecoderFilters, struct NvidiaSecondNvDecoderContext (12 members): 9.47 -> 9.44 DOWN.
                 Both leave Complex Method (cc 16 -> 14 / 15). Diagnostic, not a candidate: the second struct alone, unused, is FLAT (9.47); the drop comes from the added helper method.
                 A shared decoder struct for both blocks needs 14 members (too many), so it is not possible.
Smells left without a recipe: NONE. Complex Method in GetNvidiaVidFiltersPrefered (cc 16) remains; Recipe E rule 12 was tried twice and scored DOWN; Recipes G and P were not applied (no named block for them in this task).
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings)
Tests: PASS, with one flake. Full solution run: Controller 1411/1411, Integration 429 passed / 3 skipped. One failure in Jellyfin.Providers.Tests: Jellyfin.Providers.Tests.MediaInfo.AtlFlacRegressionTests.AtlParser_MalformedFlac_DoesNotHang (timeout after 5000 ms under load). Rerun alone: passed. Classed as a flake; not rerun in a loop.
Commits: none for code (no candidate scored UP). Report commit only.
Unsure about: (1) the baseline 9.25 vs 9.47 mismatch, followed the round brief; (2) the new private helper method alone appears to cost 0.03 in this file, the cause was not isolated (codehealth JSON carries no file-level breakdown); (3) the NvidiaSecond prefix was applied to the two new helper methods as well as the structs; (4) a Recipe S split was not tried, since it is out of scope for this task and the playbook says never first.
```
