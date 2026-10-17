# R2-03 - refactor `Emby.Server.Implementations/Library/LibraryManager.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **5.26** / 10 (Yellow) |
| Exit target | **10.00** |
| File size | 4333 lines (2118 lines of code) |
| Git churn | 835 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | E, G, P, S |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 43% / 47% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.cs
```

The score must read 5.26. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Number of Functions in a Single Module | 2 | S (not first) |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |

This file also trips **Number of Functions in a Single Module**. Splitting it (Recipe S) is never the first step:
work the steps below once, then split when they stall on the file's size, and keep going on the new files.

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `FindExtras` | 3497 | 12 | 4 | 2 | - |
| 2 | `UpdateDateBasedEpisodeNumbers` | 3443 | 10 | - | 2 | 2 |
| 3 | `UpdateEpisodeNumbers` | 3406 | 10 | - | 3 | - |
| 4 | `GetExtraTypeNameKey` | 3691 | 14 | - | - | - |
| 5 | `UpdateImagesAsync` | 2609 | 14 | - | - | - |
| 6 | `DeleteItemPath` | 655 | 12 | - | - | - |
| 7 | `GetInternalItemsPath` | 738 | 9 | - | - | - |
| 8 | `UpdateItemsAsync` | 2741 | 9 | - | - | - |
| 9 | `AddLocalAlternateVersions` | 2518 | - | - | 2 | - |
| 10 | `FindExtras.PrepareExtra` | 3579 | - | - | - | 2 |
| 11 | `GetCollectionFolders` | 2902 | - | - | 2 | - |
| 12 | `RegisterItem` | 325 | - | - | 2 | - |
| 13 | `RerouteLinkedChildReferencesAsync` | 4265 | - | - | 2 | - |
| 14 | `ResolveAlternateVersion` | 903 | - | - | 2 | - |
| 15 | `ResolveIntro` | 2270 | - | - | 3 | - |
| 16 | `SetTopParentIdsOrAncestors` | 2067 | - | - | - | 2 |
| 17 | `AddParts` | 282 | - | - | - | - |
| 18 | `GetNamedView` | 3110 | - | - | - | - |
| 19 | `LibraryManager` | 138 | - | - | - | - |
| 20 | `ResolveFileList` | 1110 | - | - | - | - |
| 21 | `ResolvePath` | 964 | - | - | - | - |
| 22 | `ResolvePaths` | 1074 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `FindExtras` (line 3497)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(LibraryManager): simplify FindExtras`

### Step 2: `UpdateDateBasedEpisodeNumbers` (line 3443)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(LibraryManager): simplify UpdateDateBasedEpisodeNumbers`

### Step 3: `UpdateEpisodeNumbers` (line 3406)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(LibraryManager): simplify UpdateEpisodeNumbers`

### Step 4: `GetExtraTypeNameKey` (line 3691)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify GetExtraTypeNameKey`

### Step 5: `UpdateImagesAsync` (line 2609)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify UpdateImagesAsync`

### Step 6: `DeleteItemPath` (line 655)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify DeleteItemPath`

### Step 7: `GetInternalItemsPath` (line 738)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify GetInternalItemsPath`

### Step 8: `UpdateItemsAsync` (line 2741)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify UpdateItemsAsync`

### Step 9: `AddLocalAlternateVersions` (line 2518)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify AddLocalAlternateVersions`

### Step 10: `FindExtras.PrepareExtra` (line 3579)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify FindExtras.PrepareExtra`

### Step 11: `GetCollectionFolders` (line 2902)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify GetCollectionFolders`

### Step 12: `RegisterItem` (line 325)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify RegisterItem`

### Step 13: `RerouteLinkedChildReferencesAsync` (line 4265)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify RerouteLinkedChildReferencesAsync`

### Step 14: `ResolveAlternateVersion` (line 903)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify ResolveAlternateVersion`

### Step 15: `ResolveIntro` (line 2270)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify ResolveIntro`

### Step 16: `SetTopParentIdsOrAncestors` (line 2067)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify SetTopParentIdsOrAncestors`

### Step 17: `AddParts` (line 282)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify AddParts`

### Step 18: `GetNamedView` (line 3110)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify GetNamedView`

### Step 19: `LibraryManager` (line 138)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify LibraryManager`

### Step 20: `ResolveFileList` (line 1110)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolveFileList`

### Step 21: `ResolvePath` (line 964)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolvePath`

### Step 22: `ResolvePaths` (line 1074)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolvePaths`

### File-level smells

- *Number of Functions in a Single Module*: S (not first)

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
dotnet test tests/Jellyfin.Server.Implementations.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py Emby.Server.Implementations/Library/LibraryManager.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/LibraryManager.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together Emby.Server.Implementations/Library/LibraryManager.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R2-03 (Emby.Server.Implementations/Library/LibraryManager.cs)
Baseline score: 5.26
Final score:    <x.xx>
Steps completed: <n> of 22
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
