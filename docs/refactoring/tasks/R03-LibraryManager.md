# R03 - refactor `Emby.Server.Implementations/Library/LibraryManager.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **2.03** / 10 (Red) |
| Exit target | **10.00** |
| File size | 4234 lines (2092 lines of code) |
| Git churn | 797 commits |
| Part | library (`Emby.Server.Implementations/Library`) |
| Recipes needed | D, E, G, P |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments, Number of Functions in a Single Module |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 43% / 47% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py Emby.Server.Implementations/Library/LibraryManager.cs
```

The score must read 2.03. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Number of Functions in a Single Module | 2 | none - report it |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Code Duplication | 2 | D |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |

## 4. What must not change here

- Public members may be called from the tests or other projects. The solution build, which
  includes every test project, tells you if you broke one: never change a test to fix it.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `CreateItems` | 2446 | 19 | 5 | 3 | - |
| 2 | `UpdateItemsAsync` | 2660 | 19 | 5 | 4 | - |
| 3 | `DeleteItem` | 421 | 41 | - | 6 | 4 |
| 4 | `FillMissingEpisodeNumbersFromPath` | 3305 | 31 | - | 8 | 2 |
| 5 | `FindExtras` | 3442 | 15 | 4 | 2 | - |
| 6 | `GetTopParentIdsForQuery` | 2132 | 14 | - | 3 | 3 |
| 7 | `GetNamedView` | 3054 | 13 | - | - | - |
| 8 | `AddUserToQuery` | 2090 | 11 | - | - | 7 |
| 9 | `SavePeopleMetadataAsync` | 3882 | 10 | - | 2 | - |
| 10 | `AddVirtualFolder` | 3807 | 9 | - | 2 | - |
| 11 | `ConvertImageToLocal` | 3751 | 9 | - | - | 2 |
| 12 | `FindExtras.PrepareExtra` | 3508 | 9 | - | - | 3 |
| 13 | `SetTopParentIdsOrAncestors` | 2043 | 9 | - | - | 4 |
| 14 | `Sort` | 2341 | 9 | - | - | - |
| 15 | `UpdateImagesAsync` | 2556 | 20 | - | - | - |
| 16 | `GetExtraTypeNameKey` | 3621 | 14 | - | - | - |
| 17 | `DeleteItemPath` | 627 | 13 | - | - | - |
| 18 | `DeleteItemsUnsafeFast` | 361 | 13 | - | - | - |
| 19 | `ResolvePath` | 926 | 13 | - | - | - |
| 20 | `IsInternalItem` | 684 | 12 | - | - | - |
| 21 | `RerouteLinkedChildReferencesAsync` | 4168 | - | 4 | - | - |
| 22 | `GetCollectionFolders` | 2846 | - | - | 2 | - |
| 23 | `GetCount` | 1764 | - | - | - | - |
| 24 | `GetItemCounts` | 1783 | - | - | - | - |
| 25 | `GetItemList` | 1740 | - | - | - | - |
| 26 | `GetLatestItemList` | 1852 | - | - | - | - |
| 27 | `GetNextUpSeriesKeys` | 1867 | - | - | - | - |
| 28 | `GetShadowView` | 3131 | - | - | - | - |
| 29 | `ImageNeedsRefresh` | 2536 | - | - | - | 2 |
| 30 | `RegisterItem` | 325 | - | - | 2 | - |
| 31 | `RemoveContentTypeOverrides` | 4081 | - | - | - | 2 |
| 32 | `ResolveAlternateVersion` | 865 | - | - | 2 | - |
| 33 | `ResolveIntro` | 2243 | - | - | 3 | - |
| 34 | `AddParts` | 282 | - | - | - | - |
| 35 | `LibraryManager` | 138 | - | - | - | - |
| 36 | `ResolveFileList` | 1067 | - | - | - | - |
| 37 | `ResolvePaths` | 1031 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `CreateItems` (line 2446)

- **Recipe G (guard clauses)** - nesting is 5, target is under 4.
- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify CreateItems`

### Step 2: `UpdateItemsAsync` (line 2660)

- **Recipe G (guard clauses)** - nesting is 5, target is under 4.
- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify UpdateItemsAsync`

### Step 3: `DeleteItem` (line 421)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify DeleteItem`

### Step 4: `FillMissingEpisodeNumbersFromPath` (line 3305)

- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify FillMissingEpisodeNumbersFromPath`

### Step 5: `FindExtras` (line 3442)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify FindExtras`

### Step 6: `GetTopParentIdsForQuery` (line 2132)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify GetTopParentIdsForQuery`

### Step 7: `GetNamedView` (line 3054)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify GetNamedView`

### Step 8: `AddUserToQuery` (line 2090)

- **Recipe P (named predicate)** - 7 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify AddUserToQuery`

### Step 9: `SavePeopleMetadataAsync` (line 3882)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify SavePeopleMetadataAsync`

### Step 10: `AddVirtualFolder` (line 3807)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify AddVirtualFolder`

### Step 11: `ConvertImageToLocal` (line 3751)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify ConvertImageToLocal`

### Step 12: `FindExtras.PrepareExtra` (line 3508)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify FindExtras.PrepareExtra`

### Step 13: `SetTopParentIdsOrAncestors` (line 2043)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify SetTopParentIdsOrAncestors`

### Step 14: `Sort` (line 2341)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.
- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify Sort`

### Step 15: `UpdateImagesAsync` (line 2556)

- **Recipes G, E, P** - cyclomatic complexity 20; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify UpdateImagesAsync`

### Step 16: `GetExtraTypeNameKey` (line 3621)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify GetExtraTypeNameKey`

### Step 17: `DeleteItemPath` (line 627)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify DeleteItemPath`

### Step 18: `DeleteItemsUnsafeFast` (line 361)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify DeleteItemsUnsafeFast`

### Step 19: `ResolvePath` (line 926)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolvePath`

### Step 20: `IsInternalItem` (line 684)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.

Commit message: `refactor(LibraryManager): simplify IsInternalItem`

### Step 21: `RerouteLinkedChildReferencesAsync` (line 4168)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.

Commit message: `refactor(LibraryManager): simplify RerouteLinkedChildReferencesAsync`

### Step 22: `GetCollectionFolders` (line 2846)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify GetCollectionFolders`

### Step 23: `GetCount` (line 1764)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetCount`

### Step 24: `GetItemCounts` (line 1783)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetItemCounts`

### Step 25: `GetItemList` (line 1740)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetItemList`

### Step 26: `GetLatestItemList` (line 1852)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetLatestItemList`

### Step 27: `GetNextUpSeriesKeys` (line 1867)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetNextUpSeriesKeys`

### Step 28: `GetShadowView` (line 3131)

- **Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.

Commit message: `refactor(LibraryManager): simplify GetShadowView`

### Step 29: `ImageNeedsRefresh` (line 2536)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify ImageNeedsRefresh`

### Step 30: `RegisterItem` (line 325)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify RegisterItem`

### Step 31: `RemoveContentTypeOverrides` (line 4081)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(LibraryManager): simplify RemoveContentTypeOverrides`

### Step 32: `ResolveAlternateVersion` (line 865)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify ResolveAlternateVersion`

### Step 33: `ResolveIntro` (line 2243)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(LibraryManager): simplify ResolveIntro`

### Step 34: `AddParts` (line 282)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify AddParts`

### Step 35: `LibraryManager` (line 138)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify LibraryManager`

### Step 36: `ResolveFileList` (line 1067)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolveFileList`

### Step 37: `ResolvePaths` (line 1031)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(LibraryManager): simplify ResolvePaths`

### File-level smells

- *Number of Functions in a Single Module*: no recipe in the playbook. Report it.
- *Overall Code Complexity*: follows the functions

### Final step: re-measure, then stop

Once the score reaches 10.00, **stop**.

## 7. Verification - run all four after EVERY step

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
- `FLAT` or `DOWN`: revert that step with `git checkout -- Emby.Server.Implementations/Library/LibraryManager.cs` and move to the next method.
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
Task: R03 (Emby.Server.Implementations/Library/LibraryManager.cs)
Baseline score: 2.03
Final score:    <x.xx>
Steps completed: <n> of 37
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
