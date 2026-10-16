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

1. **One recipe, one method, one commit.** Never combine two recipes in one commit.
2. **Re-measure after every commit.** If the Code Health score did not improve, revert.
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

---

## Recipe D - Deduplicate

**Use when:** CodeScene reports *Code Duplication*.

**How:** only when the blocks are **identical or differ by a single value**. Extract one
`private` helper and pass the differing value as a parameter. If they differ in two or
more places, leave them alone - forcing a shared abstraction over near-miss duplicates is
how behaviour gets broken.

---

## Recipe S - Split File

**Use when:** CodeScene reports *Lines of Code in a Single File*.

**This is the last recipe you apply, never the first.** Reduce method complexity first;
the file often drops below the limit without needing a split at all.

**How:**

1. Group methods that share a private field or an obvious theme.
2. Mark the class `partial` (if it is not already) and move that group to a new file
   beside the original, named `<ClassName>.<Theme>.cs`.
3. Move members only; the class, its namespace, its fields and the moved members'
   accessibility stay exactly as they were.
4. Re-run `dotnet build Jellyfin.sln` and confirm the analyzers accept the new file.

Do not split a file if it would require making a `private` member `internal` or `public`,
or moving members to another class. Widening accessibility is a behaviour change in the
sense that matters here: it changes what the rest of the program can reach.

---

## The verification loop

After **every** commit, run the four checks your task file lists:

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

Recipe D removes a duplicated block, so it trips this guard by design. That recipe needs
human review before it lands.

`codehealth_check.py` measures the file now and as committed, prints the smells that remain,
and ends with a verdict:

| Result | Action |
| --- | --- |
| Build fails | Fix it, or revert. Never commit a broken build. |
| Tests fail | `git checkout -- <file>`. No exceptions. Never change a test. |
| Guard FAILs | `git checkout -- <file>`. No exceptions. |
| `UP` | Keep it. Commit. Next method. |
| `FLAT` or `DOWN` | `git checkout -- <file>`. Next method. |

The guard checks constants, not control flow, and the tests catch only some behaviour
changes. An inverted condition with no literal change can pass both - so they reduce risk,
they do not remove it. That is why files the tests barely cover carry a gate in their task
file.
