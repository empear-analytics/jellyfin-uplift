# Refactoring Playbook

This is a **closed catalogue**. If a change you want to make is not one of the recipes
below, you are not allowed to make it as part of this campaign. Stop and report instead.

Every recipe here is *behaviour-preserving*: it moves code around without changing what
the program computes. That property is what makes this campaign safe to run at scale.
The test suite catches many behaviour changes, but not all of them: roughly half of the
seeded mutants in the scope survive it (see [`test-harness.md`](test-harness.md)). The
recipes are the protection; the tests are the backstop.

---

## The three rules

1. **One recipe, one method, one commit.** Never combine two recipes in one commit. One
   commit may apply the same recipe several times within the same method, for example two
   extractions from one method. The one exception to "one method" is Recipe D across
   several methods (see Recipe D).
2. **Re-measure after every step, before committing it.** If the Code Health score did not
   improve, revert. A step that raises the score but brings a smell category new to the file,
   makes an already flagged method worse, or creates a method with *Excess Number of Function
   Arguments*, is reverted too (verdict `WORSE`).
3. **When in doubt, stop and report.** An unfinished task is fine. A silently broken
   media server is not.

---

## Absolutely forbidden

These will change behaviour, or break code outside this file, and must never appear in a
campaign commit:

- Changing any numeric literal, string literal, character literal, or enum member value.
- Changing arithmetic (`+ - * / %`), bit operations, or shifts.
- Changing a comparison operator, including `<` to `<=`. The one exception is the exact
  logical complement a guard clause needs (Recipe G).
- Reordering statements that have side effects (assignments, I/O, method calls, `await`).
- Changing types, including width (`int` to `long`), signedness, nullability (`string` to
  `string?`), and collection types (`List<T>` to `IReadOnlyList<T>`).
- "Fixing" a bug you spot. **Report it, leave it.** The campaign preserves behaviour,
  including behaviour that looks wrong.
- Deleting code that looks dead. It may be reached through dependency injection, reflection,
  a plugin, or an HTTP request.
- Touching a static data table (`static readonly` arrays, dictionaries and sets of
  constants). They are out of scope entirely.
- Renaming, removing, or changing the signature or accessibility of any member that is not
  `private`. Public and protected members of `MediaBrowser.Controller` and `MediaBrowser.Model`
  are plugin API; other non-private members may be called from other projects or from tests.
- Renaming or changing the internal members the existing tests call (listed in your task
  file). Keep each one; you may move its body.
- Modifying anything under `tests/`.

---

## Recipe E - Extract Method

**Use when:** CodeScene reports *Bumpy Road Ahead* (a method with several separate
blocks of nested logic). Each "bump" is a missing method.

**How:**

1. Find one nested block. It usually already has a comment or a blank line around it.
2. Cut it into a new `private` method, placed after the current one (StyleCop wants
   private members after public ones). Make it `static` if it uses no instance members.
3. Pass in every variable it reads as a parameter. Return the single value it produces.
4. If the block writes to more than one outer variable, **skip it** and move to the next
   bump. Do not invent `out` parameters, tuples, or a result class.
5. If the block contains `await`, the new method is `async` and the call is awaited where
   the block was. Keep every `.ConfigureAwait(false)` exactly as it was.
6. **A block that sets a flag only on some paths** (`changed = true;` inside an `if`) still
   produces a single value. Pass the flag in and return it:
   `changed = FillParentIndexNumber(episode, changed);`. The new method starts from the value
   passed in, so a path that does not set the flag returns it unchanged.
7. **A loop at the end of a method whose `return` statements end the method** may be
   extracted together with the method's final `return`. The call becomes
   `return FindFirstSupportedContainer(...);`. Every `return` inside the loop, and the final
   one, move unchanged. This applies only when nothing follows the loop but that final
   `return`.
8. If the block reads a nullable value that the code before it has checked, see
   [Nullable flow analysis](#nullable-flow-analysis-recipes-e-and-p).

**Before:**

```csharp
public void UpdateJob(TranscodingJob job)
{
    // ... other work ...
    if (job.HasExited)
    {
        if (job.ActiveRequestCount > 0)
        {
            job.ActiveRequestCount--;
            if (job.ActiveRequestCount == 0)
            {
                job.StartKillTimer(OnKillTimer);
            }
        }
    }
}
```

**After:**

```csharp
public void UpdateJob(TranscodingJob job)
{
    // ... other work ...
    ReleaseExitedJob(job);
}

private void ReleaseExitedJob(TranscodingJob job)
{
    if (!job.HasExited)
    {
        return;
    }

    if (job.ActiveRequestCount <= 0)
    {
        return;
    }

    job.ActiveRequestCount--;
    if (job.ActiveRequestCount == 0)
    {
        job.StartKillTimer(OnKillTimer);
    }
}
```

Name the new method for **what it does**, not where it came from. `ReleaseExitedJob`,
not `UpdateJobPart2`.

---

## Recipe G - Guard Clauses

**Use when:** CodeScene reports *Deep, Nested Complexity* (nesting depth 4 or more).

**How:** invert the outermost condition and return early (in a loop body, `continue`).
Repeat until the main body sits at one level of indentation. This is the highest-value
recipe in the campaign - it is mechanical, and it directly attacks the smell that
correlates most strongly with defects.

**Before:**

```csharp
if (stream is not null)
{
    if (stream.Type == MediaStreamType.Audio)
    {
        if (stream.Channels > 0)
        {
            DoTheRealWork(stream);
        }
    }
}
```

**After:**

```csharp
if (stream is null)
{
    return;
}

if (stream.Type != MediaStreamType.Audio)
{
    return;
}

if (stream.Channels <= 0)
{
    return;
}

DoTheRealWork(stream);
```

**Careful:** if the method returns a value, every guard must return the *same* value the
original code would have produced by falling through. Read the end of the method before
you start. If you cannot tell what it falls through to, skip the method and report it.

The complement must be exact. `x > 0` becomes `x <= 0`; a nullable comparison such as
`stream.Channels > 0` with `int? Channels` is **not** the complement of `<= 0` (both are
false for `null`), so write `!(stream.Channels > 0)` instead.

---

## Recipe P - Named Predicate

**Use when:** CodeScene reports *Complex Conditional* or *Complex Method* driven by
compound boolean expressions.

**How:** move the expression into a `private static bool` method (or `private bool` if it
needs instance members) whose name states the intent. This usually needs no other change,
and it is the safest recipe in the catalogue.

**Before:**

```csharp
if (stream.Type == MediaStreamType.Audio && stream.Channels > 2 && !stream.IsExternal && string.IsNullOrEmpty(stream.Language))
{
    ...
}
```

**After:**

```csharp
private static bool IsUnlabelledSurroundTrack(MediaStream stream)
{
    return stream.Type == MediaStreamType.Audio && stream.Channels > 2 && !stream.IsExternal && string.IsNullOrEmpty(stream.Language);
}

if (IsUnlabelledSurroundTrack(stream))
{
    ...
}
```

Copy the expression **character for character**. Do not "simplify" the boolean algebra,
and do not reorder the operands - `&&` and `?.` short-circuit, so reordering can change
which side effects run and can introduce null dereferences.

If the build then fails with CS8602 or CS8629, because the code after the condition relied
on a null check inside it, see
[Nullable flow analysis](#nullable-flow-analysis-recipes-e-and-p).

**An `out` variable the expression already declares** becomes an `out` parameter of the
predicate, when the code after the condition uses it. This is not the invented `out`
parameter Recipe E forbids: the expression had it already. Declare it as an `out` parameter
of the predicate and pass it with `out var` at the call site. Inside the predicate, the
declaration `out var x` becomes `out x`, because it now assigns the parameter; every other
character of the expression stays as it was:

```csharp
// Before
if (tags.TryGetValue(key, out var val) && (DateTime.TryParse(val, out var dateTime) || DateTime.TryParseExact(val, "yyyy", null, DateTimeStyles.None, out dateTime)))
{
    return dateTime;
}

// After
if (tags.TryGetValue(key, out var val) && IsParsableDate(val, out var dateTime))
{
    return dateTime;
}

private static bool IsParsableDate(string val, out DateTime dateTime)
{
    return DateTime.TryParse(val, out dateTime) || DateTime.TryParseExact(val, "yyyy", null, DateTimeStyles.None, out dateTime);
}
```

---

## Nullable flow analysis (Recipes E and P)

The build treats nullable warnings as errors. Moving a null check into a predicate, or moving
code away from the check that guards it, hides that check from the compiler: CS8629
(`Nullable value type may be null`) or CS8602 (`Dereference of a possibly null reference`).
Two changes are allowed to make such a step compile. Nothing else is: never add `!`, never
add a null check, never change a type.

**1. A null check on a parameter of the predicate.** When the predicate's expression checks
one of its own parameters (`bitrate.HasValue && ...`, `dvProfile is null || ...`), mark that
parameter with `[NotNullWhen(...)]` from `System.Diagnostics.CodeAnalysis`:

- `[NotNullWhen(true)]` when the predicate can return `true` only if the parameter is not null
  (`x.HasValue && ...`, `x is not null && ...`);
- `[NotNullWhen(false)]` when it can return `false` only if the parameter is not null
  (`x is null || ...`, `!x.HasValue || ...`).

The attribute is read only by the compiler. The expression stays character for character.

```csharp
if (IsLossyAudioBitrate(bitrate, codec))
{
    args += " -ab " + bitrate.Value.ToString(CultureInfo.InvariantCulture);
}

private static bool IsLossyAudioBitrate([NotNullWhen(true)] int? bitrate, string codec)
{
    return bitrate.HasValue && !EncodingHelper.LosslessAudioCodecs.Contains(codec, StringComparison.OrdinalIgnoreCase);
}
```

**2. A member path checked before the moved code.** When the moved expression or block reads
a path such as `state.VideoRequest` that the code before it has already checked for null
(`if (state.VideoRequest is null) { return; }`), pass that path as its own parameter, with its
non-nullable type, and replace exactly that path by the parameter's name inside the moved code.
Every other character stays as it was. Only when all of these hold:

- the moved code only reads the path; it never assigns to it or to any part of it;
- the path is a field or a property whose getter has no side effects (read its definition;
  a computed getter that only reads is fine);
- nothing between the check and the call can change the path.

```csharp
// Before, after `if (state.VideoRequest is null) { return; }`
if (state.VideoRequest.VideoBitRate.HasValue && state.VideoRequest.VideoBitRate >= sourceBitrate)

// After
if (IsRequestedBitrateAtLeastSource(state.VideoRequest, sourceBitrate))

private static bool IsRequestedBitrateAtLeastSource(VideoRequestDto videoRequest, int sourceBitrate)
{
    return videoRequest.VideoBitRate.HasValue && videoRequest.VideoBitRate >= sourceBitrate;
}
```

Still not covered, so still reported: an `out` variable the expression assigns only on some
paths (CS0177), an `out` argument that writes to an existing variable, and a pattern variable
(`is Video video`) that the code after the condition uses.

---

## Recipe D - Deduplicate

**Use when:** CodeScene reports *Code Duplication*.

**How:** only when the blocks are **identical or differ by a single value**. Extract one
`private` helper and pass the differing value as a parameter. If they differ in two or
more places, leave them alone - forcing a shared abstraction over near-miss duplicates is
how behaviour gets broken.

**A duplicate spread over several methods** is deduplicated in one commit, for every copy
at once: removing one copy at a time leaves the others flagged, so no single step can
score UP. The copies must still be identical or differ by a single value.

**An identical copy CodeScene does not report** may also call the helper (or predicate) that
an earlier step extracted from the same file, instead of getting a second helper with the same
body. The copy must be identical to the helper's body, character for character, apart from
the names of the variables passed in.

**Copies that differ only in a type** (the same body, once for `int` and once for `double`)
become one generic `private` helper. Its constraints provide exactly the operations the copies
use and nothing else (for example `IEquatable<T>` for `.Equals`, `IComparisonOperators<T, T,
bool>` for `>=` and `<=`). The body stays character for character. Only when every operation
resolves, for each of the original types, to the same member it called before: `IEquatable<T>.Equals`
is `int.Equals(int)` and `double.Equals(double)`, and the operator interfaces are the types' own
operators. Anything that would need a conversion, a cast or a different overload is not covered.

```csharp
private static bool Compare<T>(ProfileCondition condition, ProfileConditionType conditionType, T currentValue, T expected)
    where T : IEquatable<T>, IComparisonOperators<T, T, bool>
```

Removing a copy removes its literals, so check this recipe with
`python tools/refactor_guard.py --dedup <file>`: a literal may occur fewer times, as long as
it still occurs, and no value new to the file may appear.

---

## Recipe S - Split File

**Use when:** CodeScene reports *Lines of Code in a Single File* or *Number of Functions in a
Single Module*, or when Recipe E or P steps score `DOWN` or `WORSE` only because they would
bring *Number of Functions in a Single Module*.

**Never the first recipe.** Work through the task's steps once first; the file often drops
below the limit without a split. Split when the remaining steps stall on the file's size: the
smells above are reported, or E and P steps fail on *Number of Functions in a Single Module*.

**After the split, keep going.** Work the remaining steps, and retry the steps that were
`FLAT`, `DOWN` or `WORSE` before the split, on whichever file now holds each method. Measure
each later step on the file it changes.

**How:**

1. Group methods that share a private field or an obvious theme.
2. Mark the class `partial` (if it is not already) and move that group to a new file
   beside the original, named `<ClassName>.<Theme>.cs`.
3. Move members only; the class, its namespace, its fields and the moved members'
   accessibility stay exactly as they were.
4. Re-run `dotnet build Jellyfin.sln` and confirm the analyzers accept the new file.
5. Check the literals of the original and the new files as one unit:
   `python tools/refactor_guard.py --together <original> <new files>`.
6. Keep the split when the original file scores `UP`. On `WORSE`, `FLAT` or `DOWN`, try
   another grouping, or revert. Measure each new file with
   `python tools/codehealth_check.py <new file>` and list its score in your report.

The new files are part of the campaign's scope from then on. When you report a score for a
file you split, give the original file's score and each new file's score.

Do not split a file if it would require making a `private` member `internal` or `public`,
or moving members to another class. Widening accessibility is a behaviour change in the
sense that matters here: it changes what the rest of the program can reach.

---

## The verification loop

After **every** step, before you commit it, run the four checks your task file lists. The
tools compare the working tree with `HEAD`, so they must run before the commit; to re-check a
commit you already made, add `--base HEAD~1`.

```bash
dotnet build Jellyfin.sln
dotnet test tests/<the test projects in your task file> --no-build
python tools/refactor_guard.py <file>
python tools/codehealth_check.py <file>
```

An incremental build of the solution takes about a minute; the test projects of one part
take one to three minutes.

`refactor_guard.py` extracts every numeric, string, and character literal from the file
before and after your change and compares them. A **removed** literal fails the check - it
means a constant was altered, or code carrying it was deleted. **Added** literals are only a
warning; a new guard clause can legitimately bring its own value.

Recipes D and S move or remove literals by design; check them with `--dedup` and
`--together` (see the recipes).

`codehealth_check.py` measures the file now and as committed, prints the smells that remain,
and ends with a verdict:

| Result | Action |
| --- | --- |
| Build fails | Fix it, or revert. Never commit a broken build. |
| Tests fail | `git checkout -- <file>`. No exceptions. Never change a test. |
| Guard FAILs | `git checkout -- <file>`. No exceptions. |
| `UP` | Keep it. Commit. Next method. |
| `WORSE` | The score rose, but a smell is new, a method got worse, or a new method has too many arguments. `git checkout -- <file>`. Next method. |
| `FLAT` or `DOWN` | `git checkout -- <file>`. Next method. |

The guard checks constants, not control flow, and the tests catch only some behaviour
changes. An inverted condition with no literal change can pass both - so they reduce risk,
they do not remove it. That is why each task file gives the file's mutation score, and why
the few files the tests barely cover are marked as such: there, the recipes are the only
protection.

---

## Changes to this playbook

The founding version restated the five founding recipes of the 3SX campaign (3sx commit
`fea568087`) for C#. Every change since is listed here with the evidence behind it.

| Date | Change | Evidence |
| --- | --- | --- |
| 2026-10-16 | Recipe P: an `out` variable the expression already declares becomes an `out` parameter of the predicate. | Calibration probe, R32 `FFProbeHelpers.GetDictionaryDateTime`: all three agents (Claude Haiku, Sonnet and Opus) needed it, applied it, and asked independently whether P allows it. Decided by the campaign owner. |
| 2026-10-16 | Rule 1: one commit may apply the same recipe several times within the same method. | First backlog batches: agents already made several extractions per method and commit (R13, R19, R22). R14's first agent expected its two mapping `switch` expressions to score UP only if extracted together; the rerun under this rule measured FLAT (each extracted switch is itself a Complex Method), so R14 is not evidence for it. Decided by the campaign owner. |
| 2026-10-16 | Recipe D: a duplicate spread over several methods is deduplicated in one commit; an identical copy CodeScene does not report may reuse a helper extracted earlier from the same file. | R17 `EncodingJobInfo`: deduplicating one of three methods per commit is FLAT, all three at once UP (8.54 to 9.09). R18 `DotIgnoreIgnoreRule` and R14 `HlsCodecStringHelpers`: identical copies needed a second helper with the same body, because reuse counted as Recipe D without a duplication report. Decided by the campaign owner. |
| 2026-10-16 | Rule 2: a step that raises the score but brings a smell category new to the file, or raises the number of an already flagged method, is reverted (`codehealth_check.py` verdict `WORSE`). | CodeScene's PR gate on part 1 (#9) rejected two such steps although each file improved: R15 `PathExtensions` (Complex Method in `GetAttributeValue`, cc 27 to 31) and R14 `HlsCodecStringHelpers` (new String Heavy Function Arguments). Decided by the campaign owner; the two steps stay. |
| 2026-10-17 | Nullable flow analysis: a predicate parameter that the expression null-checks gets `[NotNullWhen]`; a member path checked before the moved code is passed as its own non-nullable parameter. | Recipes P and E failed to compile on CS8629 or CS8602 in R01, R02, R03, R04, R06 and R07 (for example R07 `AppendDvString` and `GetOutputVideoCodecLevel`, R06 `ApplyVideoOutputSettings`, R03 `DeleteItem`). R05 applied `[NotNullWhen(true)]` to `IsLossyAudioBitrate`, which compiled and passed all checks; R06 proposed passing `state.VideoRequest` as a parameter. Decided by the campaign owner. |
| 2026-10-17 | Recipe S: also when *Number of Functions in a Single Module* blocks Recipes E and P; never first, but no longer only last; steps continue on the split-off files. | R04 `ProbeResultNormalizer`: every E or P step after step 5 scored DOWN on that smell; the split scored UP (4.83 to 8.41). R05 `DynamicHlsController` and R02 `StreamBuilder` split likewise; in R02, seven steps that were FLAT before the split scored UP after it. R01 `GetHwaccelType` (E WORSE twice) and R03 stalled on the same smell without a split. Decided by the campaign owner. |
| 2026-10-17 | Recipe E: a block that sets a flag on some paths passes the flag in and returns it; a trailing loop whose `return` statements end the method moves with the final `return`. | R03 `SavePeopleMetadataAsync` and `FillMissingEpisodeNumbersFromPath`; R02 `SelectMostEfficientSubtitleStreamIndex` and `SelectFirstSupportedContainer`. Both shapes were reviewed as equivalent and landed in part 3 (#11). Decided by the campaign owner. |
| 2026-10-17 | Rule 2: a step that creates a method with *Excess Number of Function Arguments* is `WORSE`. | R07 added helpers with 6 and 7 arguments (`AppendBitrateVariantPlaylists`, `AppendCompatibilityPlaylists`), R05 one with 9 (`StartTranscodingForSegment`), R01 one with 5 (`HasMissingScaleInput`). They passed as UP because the file already had the category. Decided by the campaign owner. |
| 2026-10-17 | Recipe D: copies that differ only in a type become one generic helper whose constraints provide exactly the operations the copies use. | R2-06 `ConditionProcessor`: `CompareInt` and `CompareDouble` were identical apart from `int`/`double`; one `Compare<T>` with `IEquatable<T>` and `IComparisonOperators<T, T, bool>` scored UP (7.35 to 7.57) with all checks passing. Decided by the campaign owner. |
