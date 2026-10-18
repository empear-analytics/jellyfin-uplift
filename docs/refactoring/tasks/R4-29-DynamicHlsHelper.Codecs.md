# R4-29 - refactor `Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **9.09** / 10 (Green) |
| Exit target | **10.00** |
| File size | 306 lines (148 lines of code) |
| Git churn | 2 commits |
| Part | api (`Jellyfin.Api/Helpers`) |
| Recipes needed | E, G, P |
| Smells without a recipe | none |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | - / - |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs
```

The score must read 9.09. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetOutputVideoCodecLevel` | 41 | 9 | - | - | 2 |
| 2 | `GetPlaylistAudioCodecs` | 148 | 10 | - | - | - |
| 3 | `GetOutputAv1BitDepth` | 267 | - | - | - | 2 |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetOutputVideoCodecLevel` (line 41)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper.Codecs): simplify GetOutputVideoCodecLevel`

### Step 2: `GetPlaylistAudioCodecs` (line 148)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(DynamicHlsHelper.Codecs): simplify GetPlaylistAudioCodecs`

### Step 3: `GetOutputAv1BitDepth` (line 267)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(DynamicHlsHelper.Codecs): simplify GetOutputAv1BitDepth`

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
python tools/refactor_guard.py Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R4-29 (Jellyfin.Api/Helpers/DynamicHlsHelper.Codecs.cs)
Baseline score: 9.09
Final score:    9.09
Steps completed: 0 of 3 (no step scored UP; the named method GetPlaylistAudioCodecs has no UP candidate)
Steps reverted:  GetPlaylistAudioCodecs, Recipe E rule 7/9 (trailing if-chain moved to a private static
  helper PlaylistAudioCodecsRemaining, split point k = number of trailing blocks moved), each alone:
  k=1 FLAT 9.09 (method still cc 9); k=2 FLAT 9.09; k=3 FLAT 9.09 (method cc 7, no longer flagged);
  k=4 FLAT 9.09; k=5 DOWN 8.54 (new Code Duplication between the method and the helper); k=6 FLAT 9.09;
  k=7 FLAT 9.09; k=8 FLAT 9.09 (helper cc 9); k=9 FLAT 9.09 (helper cc 10); k=3 plus a second tail
  extraction of flac and alac in the same method FLAT 9.09. Recipe G: not applicable (no nesting).
  Recipe P: not applicable (no compound condition). Recipe E rule 13: not applicable (no else-if chain).
  Steps 1 and 3 of the task file (Recipe P on GetOutputVideoCodecLevel and GetOutputAv1BitDepth) were
  already applied in 358b0ab08d; they were not repeated. The score stayed 9.09 whenever the method's
  own Complex Method was cleared (k=3, 4, 6), so it appears pinned by GetOutputVideoCodecLevel (cc 9).
Smells left without a recipe: Complex Method GetOutputVideoCodecLevel (cc 9; outside the named method,
  not measured). Complex Conditional GetOutputVideoCodecLevel:44 and GetOutputAv1BitDepth:272 (the
  nullable case-3 shape already applied; the guard terms must stay at the call site). Unmeasured
  candidate: lines 57-58 of GetOutputVideoCodecLevel repeat IsHevcCodec's body (Recipe D reuse, not in
  this run's recipe list).
Build: PASS (dotnet build Jellyfin.sln --no-incremental: 0 errors, 211 warnings, the base count)
Tests: PASS (dotnet test Jellyfin.sln --no-build: all 16 projects, 0 failed; Api 156/156,
  Integration 429 passed and 3 skipped)
Commits: none for code; a docs-only commit carries this report
Unsure about: (1) Whether a trailing if-chain falls under "Recipe E rule 9/13" as the run used it.
  I read it as rule 9 (a run of statements computing one value) with rule 7's tail conditions; the
  playbook's history cites this method for rule 7. The measured result is FLAT or DOWN either way.
  (2) Test data: $TEMP/jellyfin-test-data is not fully removed. Several test-host folders hold open
  jellyfin.db files and were modified at 22:06, while other dotnet test processes ran on this machine,
  so I left them as active.
```
