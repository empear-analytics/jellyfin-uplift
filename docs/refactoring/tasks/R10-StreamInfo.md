# R10 - refactor `MediaBrowser.Model/Dlna/StreamInfo.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **5.89** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 1395 lines (499 lines of code) |
| Git churn | 174 commits |
| Part | dlna (`MediaBrowser.Model/Dlna`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Excess Number of Function Arguments, Primitive Obsession, String Heavy Function Arguments |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 52% / 74% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

The score must read 5.89. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Excess Number of Function Arguments | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |
| String Heavy Function Arguments | 2 | none - report it |

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `ToUrl` | 877 | 49 | - | 4 | 5 |
| 2 | `GetSubtitleProfiles` | 1167 | 14 | - | 2 | 2 |
| 3 | `GetSubtitleStreamInfo` | 1230 | 13 | - | - | 6 |
| 4 | `AddSubtitleProfiles` | 1207 | - | - | 2 | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `ToUrl` (line 877)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 5 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 49): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(StreamInfo): simplify ToUrl`

### Step 2: `GetSubtitleProfiles` (line 1167)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify GetSubtitleProfiles`

### Step 3: `GetSubtitleStreamInfo` (line 1230)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 13): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify GetSubtitleStreamInfo`

### Step 4: `AddSubtitleProfiles` (line 1207)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(StreamInfo): simplify AddSubtitleProfiles`

### File-level smells

- *Overall Code Complexity*: follows the functions
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
dotnet test tests/Jellyfin.Model.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Model/Dlna/StreamInfo.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Model/Dlna/StreamInfo.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Model/Dlna/StreamInfo.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R10 (MediaBrowser.Model/Dlna/StreamInfo.cs)
Baseline score: 5.89
Final score:    <x.xx>
Steps completed: <n> of 4
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
