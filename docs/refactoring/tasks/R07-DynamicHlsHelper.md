# R07 - refactor `Jellyfin.Api/Helpers/DynamicHlsHelper.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **4.59** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 999 lines (515 lines of code) |
| Git churn | 67 commits |
| Part | api (`Jellyfin.Api/Helpers`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 77% / 70% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/DynamicHlsHelper.cs
```

The score must read 4.59. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `AppendPlaylistVideoRangeField` | 442 | 10 | 4 | 2 | - |
| 2 | `GetMasterPlaylistInternal` | 112 | 54 | - | - | 17 |
| 3 | `GetPlaylistVideoCodecs` | 897 | 14 | - | - | 2 |
| 4 | `GetOutputVideoCodecLevel` | 753 | 13 | - | - | 2 |
| 5 | `GetOutputVideoCodecProfile` | 798 | 11 | - | - | 2 |
| 6 | `AppendPlaylistSupplementalCodecsField.AppendDvString` | 550 | 10 | - | - | 2 |
| 7 | `GetPlaylistAudioCodecs` | 832 | 12 | - | - | - |
| 8 | `AddSubtitles` | 675 | 9 | - | - | - |
| 9 | `EnableAdaptiveBitrateStreaming` | 638 | 9 | - | - | - |
| 10 | `AppendDoviPlaylist` | 387 | - | - | - | - |
| 11 | `AppendPlaylist` | 345 | - | - | - | - |
| 12 | `DynamicHlsHelper` | 63 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `AppendPlaylistVideoRangeField` (line 442)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper): simplify AppendPlaylistVideoRangeField`

### Step 2: `GetMasterPlaylistInternal` (line 112)

- **Recipe P (named predicate)** - 17 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 54): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsHelper): simplify GetMasterPlaylistInternal`

### Step 3: `GetPlaylistVideoCodecs` (line 897)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper): simplify GetPlaylistVideoCodecs`

### Step 4: `GetOutputVideoCodecLevel` (line 753)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 13): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper): simplify GetOutputVideoCodecLevel`

### Step 5: `GetOutputVideoCodecProfile` (line 798)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 11): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper): simplify GetOutputVideoCodecProfile`

### Step 6: `AppendPlaylistSupplementalCodecsField.AppendDvString` (line 550)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(DynamicHlsHelper): simplify AppendPlaylistSupplementalCodecsField.AppendDvString`

### Step 7: `GetPlaylistAudioCodecs` (line 832)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(DynamicHlsHelper): simplify GetPlaylistAudioCodecs`

### Step 8: `AddSubtitles` (line 675)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(DynamicHlsHelper): simplify AddSubtitles`

### Step 9: `EnableAdaptiveBitrateStreaming` (line 638)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(DynamicHlsHelper): simplify EnableAdaptiveBitrateStreaming`

### Step 10: `AppendDoviPlaylist` (line 387)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsHelper): simplify AppendDoviPlaylist`

### Step 11: `AppendPlaylist` (line 345)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsHelper): simplify AppendPlaylist`

### Step 12: `DynamicHlsHelper` (line 63)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsHelper): simplify DynamicHlsHelper`

### File-level smells

- *Overall Code Complexity*: follows the functions

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
python tools/refactor_guard.py Jellyfin.Api/Helpers/DynamicHlsHelper.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/DynamicHlsHelper.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Helpers/DynamicHlsHelper.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Jellyfin.Api/Helpers/DynamicHlsHelper.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R07 (Jellyfin.Api/Helpers/DynamicHlsHelper.cs)
Baseline score: 4.59
Final score:    <x.xx>
Steps completed: <n> of 12
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
