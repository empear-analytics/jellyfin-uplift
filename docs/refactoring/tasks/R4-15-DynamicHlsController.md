# R4-15 - refactor `Jellyfin.Api/Controllers/DynamicHlsController.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **8.16** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1222 lines (671 lines of code) |
| Git churn | 177 commits |
| Part | api (`Jellyfin.Api/Controllers`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Constructor Over-Injection, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 58% / 48% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Jellyfin.Api/Controllers/DynamicHlsController.cs
```

The score must read 8.16. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Complex Method | 2 | G, E, P |
| Excess Number of Function Arguments | 2 | E (context struct, rule 12; a signature itself is frozen) |
| Constructor Over-Injection | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- The existing tests call these internal members directly: `GetDolbyVisionHevcCodecTag`, `GetSegmentLengthsInternal`, `WaitForActiveTranscodingRequests`. Keep each one, with
  its name, signature and class. You may move its body into new private methods.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetSegmentResult` | 1030 | 10 | - | 3 | - |
| 2 | `GetLiveHlsStream` | 165 | 13 | - | - | - |
| 3 | `GetVideoEncodingArguments` | 987 | - | - | - | - |
| 4 | `StartTranscodingForSegment` | 490 | - | - | - | - |
| 5 | `DynamicHlsController` | 78 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetSegmentResult` (line 1030)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (7 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(DynamicHlsController): simplify GetSegmentResult`

### Step 2: `GetLiveHlsStream` (line 165)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (51 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(DynamicHlsController): simplify GetLiveHlsStream`

### Step 3: `GetVideoEncodingArguments` (line 987)

- *Excess Number of Function Arguments* (5 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(DynamicHlsController): simplify GetVideoEncodingArguments`

### Step 4: `StartTranscodingForSegment` (line 490)

- *Excess Number of Function Arguments* (9 arguments) - **Recipe E rule 12 (context struct)** applies to a block that needs more than four values from this method; a non-private signature itself stays frozen.

Commit message: `refactor(DynamicHlsController): simplify StartTranscodingForSegment`

### Step 5: `DynamicHlsController` (line 78)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(DynamicHlsController): simplify DynamicHlsController`

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
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Jellyfin.Api/Controllers/DynamicHlsController.cs` and move to the next method.
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
Task: R4-15 (Jellyfin.Api/Controllers/DynamicHlsController.cs)
Baseline score: 8.16
Final score:    8.16
Steps completed: 0 of 5 (no code commit; step 2, GetLiveHlsStream, has no UP candidate)
Steps reverted:  10 single Recipe P applications on GetLiveHlsStream (each FLAT, 8.16); cumulative
                 4 (FLAT, 8.16, cc 9); cumulative 5 (DOWN, 8.14: cc 8, Complex Method gone, Large
                 Method revealed on GetLiveHlsStream, 115 lines); cumulative 10 (DOWN, 8.14, same).
                 Recipe E rules 9 and 12 cannot cover the 51-value object initializer (a struct holds
                 at most 12 values, a method at most 4 parameters, so at most 26 values): not measured.
Smells left without a recipe: Constructor Over-Injection (DynamicHlsController, 11 args);
                 Primitive Obsession (file-level); Excess Number of Function Arguments on
                 GetLiveHlsStream (51, frozen signature). Complex Method on GetLiveHlsStream (cc 13):
                 Recipe P reduces cc but reveals Large Method, so it stays blocked until its line count falls.
Build: PASS (0 errors, 211 warnings, dotnet build Jellyfin.sln --no-incremental)
Tests: PASS (full solution, 17 test assemblies, 0 failed; Integration 432 total, 3 skipped)
Commits: none for code. This report commit is the only one on the detached HEAD.
Unsure about: (1) Recipe P is applied to a bool? ?? default that is not a condition (ten
                 cases: Static, EnableAutoStreamCopy, AllowAudioStreamCopy, AllowVideoStreamCopy,
                 CopyTimestamps, RequireAvc, DeInterlace, RequireNonAnamorphic, EnableMpegtsM2TsMode,
                 EnableSubtitlesInManifest). The playbook says a boolean expression carrying ?? is a
                 predicate, but it is written for conditions. None scored UP, so nothing was committed.
                 (2) The Large Method was inferred to be GetLiveHlsStream (the only method whose
                 span changed); the tool printed no method name for it.
                 Candidates for new recipes: (a) ?? defaults of non-bool type in the initializer
                 (SubtitleMethod at line 254, Context at line 269), which P does not cover; (b) a
                 single statement with more values than a Recipe E context can hold (GetLiveHlsStream
                 lines 222-276); (c) Large Method from a frozen parameter list.
                 Other methods of this file, not measured: GetSegmentResult (cc 10, Bumpy Road 3,
                 7 args), StartTranscodingForSegment (9 args), GetVideoEncodingArguments (5 args).
```
