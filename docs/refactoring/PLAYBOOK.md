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
   extractions from one method. Two exceptions to "one method": Recipe D across several
   methods (see Recipe D), and any other single recipe applied the same way to several
   methods **of one file** when no single application scores `UP` but together they do (each
   alone `FLAT`, never `DOWN`). Measure each application alone first and say so in the commit
   message, which lists the methods; every check runs on the combined change. A pair of steps of
   two different recipes in one file, each alone `FLAT` and together `UP`, may also be one commit,
   with the message naming both recipes. Never more than two recipes in one commit.
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
   bump. Do not invent `out` parameters or tuples. The one exception is rule 15 below (a result
   struct), under its conditions.
5. If the block contains `await`, the new method is `async` and the call is awaited where
   the block was. Keep every `.ConfigureAwait(false)` exactly as it was.
6. **A block that sets a flag only on some paths** (`changed = true;` inside an `if`) still
   produces a single value. Pass the flag in and return it:
   `changed = FillParentIndexNumber(episode, changed);`. The new method starts from the value
   passed in, so a path that does not set the flag returns it unchanged.
7. **A tail of the method whose every path ends in `return` or `throw`** may be extracted as
   one method, and the call becomes `return M(...);`. The tail is the last statements of the
   method body: a loop followed by the final `return`, a chain of `if (...) { return X; }`
   statements followed by a final `return` or `throw`, or a nested block that returns on every
   path. Every statement of the tail moves unchanged, in the same order, so the checks are
   made in the same order as before. Further conditions:
   - the tail does not assign an `out` or `ref` parameter of the original method, and it
     contains no `yield`;
   - a variable the tail assigns before it reads it, and that nothing outside the tail reads
     (nothing can: the tail ends the method), is declared inside the new method with its
     original type, for example `string args = ...;` where the original had `args = ...;`. If
     the tail reads the variable's earlier value, pass it in as for any other variable;
   - a tail that moves only part of a chain moves the last statements of the chain, never a
     middle part.
8. If the block reads a nullable value that the code before it has checked, see
   [Nullable flow analysis](#nullable-flow-analysis-recipes-e-and-p).
9. **The block need not be nested.** A run of consecutive statements that forms one step
   (a log call, building one string, computing one value) may be extracted under the rules
   above when it produces a single value or none. Keep the statements and their order as they
   were. This is allowed because a single complex statement can carry the complexity (for
   example many `??` operators); the step must still score `UP`. When the block calls a local
   function of the method, the new method is itself a local function, declared next to the one it
   calls (a private method cannot reach a local function); it takes the values it reads as
   parameters, as a private method would.
10. **A block that leaves a loop iteration with `continue`.** A block inside a loop body, in which
    some paths end in `continue` and the others fall out of the block, moves to a method that
    returns `bool`. Each `continue;` that targeted the enclosing loop becomes `return true;`, the
    end of the block becomes `return false;`, and the call site is
    `if (Helper(...)) { continue; }`. Every other statement moves unchanged, in the same order.
    Only when the block contains no loop of its own, no `return` or `yield`, and no `break` that
    leaves anything but a `switch` inside the block (a `switch` statement of its own is allowed:
    its `break`s stay as they are, and each `continue;` inside it becomes `return true;`), and
    assigns no local variable that is read after the block (it may change objects it is
    passed, such as a list, as rule 9 allows). If the block contains `await`, the method is
    `async Task<bool>`.
11. **A large `switch` expression: a suffix of its arms.** The last arms of the switch, together
    with its discard arm, move to a new method that returns the switch expression of those arms;
    the original keeps its first arms and gets the discard arm `_ => Helper(x)`. The arms keep their
    order, guards (`when`) and patterns, so the first matching arm is the same as before, and the
    original's discard arm (including a `throw`) moves unchanged into the helper. Never take arms
    from the middle. The same applies to a `switch` **statement** whose sections end in `return`
    (or `throw`): the last sections, together with `default:`, move to a new method holding a
    `switch` statement of those sections, and the original's `default:` becomes
    `default: return Helper(x);`. Sections that fall through to each other (stacked `case` labels)
    move together. Measure the split point: a helper with more than about five sections can stay
    a Complex Method. The same applies to a `switch` **statement** whose sections end in `break`
    (no `return`): the last sections, with `default:`, move to a helper holding a `switch`
    statement of those sections, each section keeping its `break`, and the original's `default:`
    becomes `default: Helper(x); break;`. Only whole sections move, and nothing after the switch
    depends on a section that moved.
    **Mixed sections.** When the switch statement is directly followed by `return v;` as the last
    statement of the method (`v` a constant, or a local the switch does not assign), its sections may
    end in `return` or in `break`: a suffix of whole sections, with `default:`, moves to a helper
    holding a switch of those sections, each keeping its `return` or `break`, and the helper ends
    with the same `return v;`; the original's `default:` becomes `default: return Helper(x);`. A
    single section of that switch may also move on its own: `case X: return HelperX(...);`, the
    helper holding the section's statements unchanged, its `break` becoming the helper's final
    `return v;`. Only when nothing stands between the switch and `return v;`, and no section holds
    `continue` or `goto`. Same conditions as above otherwise.
12. **Shared inputs (a context struct).** A block that needs more than four values from its method
    may take them as one `private readonly record struct`, nested in the class at the end of the
    file that holds the method. A method may have one such struct per family of blocks that need
    different sets of values (for example the subtitle blocks, the software-decoder blocks and the
    hardware-decoder blocks), at most three; each struct serves one family of blocks and is
    constructed immediately before the first block that uses it.
    Only when:
    - the members are values the block only reads: locals that are not assigned between the
      struct's construction and the end of the block, and references (a list, the `state` object)
      whose contents the block may change, as before; never a local the block assigns;
    - each struct is constructed once, with `new FooContext(...)`, immediately before the first
      block that uses it, and later blocks of its family reuse it;
    - inside the moved code, each local `x` that became a member is replaced by `context.X` (the
      same name with an upper-case first letter), and nothing else changes;
    - the new method has at most four parameters, the context included, and the struct at most
      twelve members.
    The struct is `private`, so no accessibility is widened. Move the whole statement that holds
    the block (for example the whole `if (hasSubs) { ... }`, with the flag as a member of the
    context and `state` as a plain parameter), not only its inside: leaving the guard at the call
    site keeps the nesting and the bumps. A value used inside an interpolated string stays a plain
    parameter when the guard reports the string as changed.
    Three additions of 2026-10-21, all plumbing that changes no behaviour:
    - A helper may take two structs of its method when its block needs values from both; it still
      has at most four parameters, and each struct is still constructed once, in the original
      method.
    - When the whole statement needs more values than fit (twelve members and three plain
      parameters beside the context, fifteen in all), the guard may stay at the call site and only
      the inside moves. Measure it: this form is allowed only when the whole statement does not fit.
    - Write the construction of a struct on one line: the score follows the method's line count, and
      a construction split over fourteen lines scored 9.46 where the same one on a line scored 9.52.
13. **The tail of an `if` / `else if` chain.** The last branches of a chain
    (`if (a) { ... } else if (b) { ... } else if (c) { ... } else if (d) { ... }`) move, from some
    `else if` onwards, to a new method; the original gets `else { x = Helper(...); }` (when the
    branches assign a variable, which is then passed in and returned) or `else { return
    Helper(...); }` (when every branch returns). The conditions and bodies keep their order and
    their text, so the first matching condition is the same as before; a condition that is
    already true on the way in (such as a repeated test of the same value) stays as written. Only
    when the moved branches assign or return the same single variable, change no other local that
    is read afterwards, and the new method has at most four parameters. It is a form of rules 7
    and 9, written out because it is the most common blocker in `else if` dispatch code.
    The tail may also be void: when the moved branches only add to or change objects passed in
    (such as a list), and assign no local read afterwards, the new method returns nothing and the
    original gets `else { Helper(...); }`.
14. **A private method with too many parameters (a parameter object).** A contiguous run of a
    `private` method's parameters, kept in their order, becomes one `private readonly record struct`
    nested at the end of the file that holds the method; every call site passes `new FooParts(...)`
    in place of that run, with the same argument expressions in the same order, so each argument is
    still evaluated once, left to right. Inside the method each parameter `x` of the run is replaced
    by `parts.X` (the same name with an upper-case first letter), and nothing else changes. Only when:
    - the method is `private` (not `public`, `protected` or `internal`: those may be called by plugins
      or tests), not `virtual`, `override`, `abstract` or part of an interface, and is never used as a
      method group, a delegate or by name (`nameof`); other methods of the class may share its name
      (overloads), because after the change only the call sites that build the new struct can bind
      to it, but the build must pass with no new warning;
    - every call site is in the same class, uses positional arguments for the run, and passes no
      named argument inside it;
    - no parameter of the run is `ref`, `out`, `in` or `params` or has a default value; a parameter of
      the run that the method assigns is first copied into a local of the same name, as the first
      statement of the method (`var a = parts.A;`), and the body keeps using `a`;
    - a method may have at most two runs, each contiguous and in order, each with its own struct;
    - the struct has at most twelve members, the method ends with at most four parameters, and each
      construction is written on one line.
    The method keeps its name, return type and accessibility. Public and protected signatures stay
    frozen; this rule never applies to them.
15. **A block that writes two or three locals read later (a result struct).** The block moves to a
    method that takes its inputs (plain parameters, or a rule 12 context) and the current values of
    the locals it writes, as one `private readonly record struct` (`FooResult`); the method's first
    statements copy them into locals of the same names (`var percent = current.Percent;`), the block
    follows unchanged, and the method returns `new FooResult(percent, position)`. The call site is
    `var result = Foo(..., new FooResult(percent, position));` followed by one assignment per local
    (`percent = result.Percent;`), in the struct's order. Only when:
    - the block contains no `return`, `break`, `continue`, `goto`, `yield` or `await`;
    - each written local is definitely assigned before the block, and none is captured by a lambda
      or local function anywhere in the method, or passed as `ref` or `out`;
    - the struct has at most three members, the method at most four parameters, and each
      construction is on one line.
    Every other local the block writes must be declared inside it and not read after it.
    **In a loop.** Two forms allow `continue` (and only `continue` that targets the enclosing loop):
    - when the block is the **whole body** of the loop, each `continue;` becomes
      `return new FooResult(...current values...);`, the end of the body returns the same, and the call
      site is the assignment of each local from the result, with nothing after it in the body;
    - otherwise the struct gets one more member, `bool Skipped` (so at most two written locals plus
      `Skipped`, three members in all): each `continue;` becomes
      `return new FooResult(...current values..., true);`, the end of the block returns
      `new FooResult(..., false)`, and the call site assigns each local from the result and then
      runs `if (result.Skipped) { continue; }`.

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

The expression need not be built from `&&` and `||`: a boolean expression that carries its
complexity in `??`, `?:` or `?.` operators (for example `IsBitrateLimitExceeded(item,
options.GetMaxBitrate(true) ?? 0)`) is a predicate too. The step must still score `UP`.

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

The predicate may take **only a leading run of the terms** of an `||` or `&&` chain, with the
remaining terms staying at the call site in their original order (`P(a || b || c) || d`): both
operators are associative and short-circuit left to right, so the order of evaluation is the same.
Never take a run from the middle or the end of the chain.

If the build then fails with CS8602 or CS8629, because the code after the condition relied
on a null check inside it, see
[Nullable flow analysis](#nullable-flow-analysis-recipes-e-and-p).

**An `out` variable the expression assigns only on some paths** (`TryParse(a, out x) &&
TryParse(b, out y) && ...`) fails to compile as an `out` parameter with CS0177, because the
compiler wants every `out` parameter assigned on every path. The predicate then starts with one
statement `x = default;` for each such parameter and returns the expression unchanged. The caller
reads the `out` variables only where it read them before, after the condition is true, so the
values are the same:

```csharp
private static bool TryParseDimensions(string[] parts, out int w, out int h)
{
    w = default;
    h = default;
    return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w > 0 && h > 0;
}
```

**A predicate that calls a local function.** When the expression calls a local function of the
method, the predicate is a local function too, declared next to the one it calls, taking the
values it reads as parameters. A private method cannot reach a local function.

**An `out` variable the expression already declares** becomes an `out` parameter of the predicate,
when the code after the condition uses it. This is not the invented `out`
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
Three changes are allowed to make such a step compile. Nothing else is: never add `!`, never
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

**3. A property of an object, null-checked inside the expression.** When the expression checks
a nullable-valued property of an object (`state.VideoStream.Level.HasValue`) and the code after
the condition reads that property's `.Value`, move only the part that checks the property, and
pass the property as a parameter marked `[NotNullWhen(true)]` (or `[NotNullWhen(false)]`, as
in rule 1):

- the leading terms of the expression that guard the object itself (`state.VideoStream is not
  null &&`) **stay at the call site, unchanged and first**, so the property is read only when
  the guard allows it, exactly as before;
- the terms that move are the remaining terms of the same `&&` (or `||`) chain, character for
  character, except that the property path is replaced by the parameter's name;
- the property is read with no side effects (read its getter), as in rule 2;
- if the expression has no guard term (the object cannot be null at that point), the whole
  expression moves with the property as a parameter.

```csharp
// Before
if (EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
    && state.VideoStream is not null
    && state.VideoStream.Level.HasValue
    && state.VideoStream.Level > 150)
{
    use(state.VideoStream.Level.Value);
}

// After
if (EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
    && state.VideoStream is not null
    && IsLevelAbove150(state.VideoStream.Level))
{
    use(state.VideoStream.Level.Value);
}

private static bool IsLevelAbove150([NotNullWhen(true)] int? level)
{
    return level.HasValue && level > 150;
}
```

The step is kept only if it still scores `UP`: a smaller conditional that leaves the guard
terms behind may not lower the method's complexity enough.

Still not covered, so still reported: an `out` argument that writes to an existing variable, and a
pattern variable (`is Video video`) that the code after the condition uses. (An `out` variable the
expression assigns only on some paths is covered by Recipe P, `x = default;`.)

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
the names of the variables passed in. The helper may also live in **another part of the same
partial class**, when it is `private` (or already as accessible as the code that will call it:
never widen it) and its body, read in its own file, is identical to the copy. Run the guard with
`--dedup` over the file you change, **and `--together` with the file that holds the helper** (the
guard otherwise sees the helper's literals only in the other file and rejects the step); a literal
that only the removed copy carried must still occur in the class.

**Copies that differ in a single value, without a duplication report.** Blocks or loops that
are identical except for one constant or enum member (three `foreach` loops that differ only in
a `PersonKind` value) may be merged into one `private` helper that takes that value as a
parameter, even when CodeScene reports no *Code Duplication*. The step must still score `UP`,
and the other conditions of this recipe hold: the copies are identical character for character
apart from that one value and the names of the variables passed in, and each call site passes
the value its copy used. The value moves from the body to the call sites, so the guard is run
with `--dedup` and every value must still occur.

**Copies that differ in two values.** Copies identical character for character except for two
values (a base path and a log-message literal) may be merged into one `private` helper that takes
those two values as parameters, when the helper has at most four parameters in all and each call
site passes the values its own copy used. The values move to the call sites, so the guard runs
with `--dedup` and every value must still occur. Three or more differing values stay as they are.
**A logging message template is never one of the values:** analyzer CA2254 requires a constant
template at every logging call site, so a helper that takes the template as a parameter fails the
build. When the copies differ in a log template, share only the part without the log call (for
example the GUID formatting and the path join) and keep each log call, with its own literal, in
its copy.

**Branches that differ in one value.** When the two branches of one `if` / `else` are identical
character for character except for one value (the needle searched for, a prefix, a limit), the
value is chosen first and one copy of the body stays:

```csharp
var searched = shortAttr.Length > 0 ? shortAttr : attribute;
attributeIndex = subStr.IndexOf(searched, StringComparison.OrdinalIgnoreCase);
attributeEnd = attributeIndex + searched.Length;
```

Only when the condition and both values are free of side effects (each is evaluated once either
way), the branches are the only two (`if` and `else`, no `else if`), and the new local has a name
not used in the method. Both values stay in the code, so the guard runs with `--dedup` and every
literal must still occur. The step must score `UP`. It does not lower cyclomatic complexity (CodeScene
counts the `?:` as a branch, like the `if` it replaces); it helps only where it removes nesting: when
each branch holds a nested conditional (`if (q) { if (!a) return true; } else { if (!b) return true; }`
becomes `if (!(q ? a : b)) return true;`), or when the `if` / `else` itself sits inside another block.

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
bring *Number of Functions in a Single Module* or *Overall Code Complexity* (the average
complexity of the file's methods: one more method pushes it over, and every later step then
scores `DOWN`).

**Never the first recipe.** Work through the task's steps once first; the file often drops
below the limit without a split. Split when the remaining steps stall on the file's size or
on those file-level smells.

**A split has a floor.** Some groupings bring *Low Cohesion* instead of clearing the smell
(R2-03). Try at most two groupings; keep the first one under which the original file scores
`UP`. If none does, revert and report the file as blocked.

**A split that is `FLAT` may be kept when it enables a step.** A file can sit just below a
file-level smell (*Overall Code Complexity*, *Code Duplication* that the extra methods would
reveal, *Primitive Obsession*): then a split scores `FLAT`, and so does every extraction before it,
but after the split the extractions score `UP`. Keep such a split (never one that scores `DOWN`)
only as a pair: apply the split, then the best step on top of it, and measure both against the score
**before the split**. If the pair scores `UP` against that score, commit the split first
(commit message: `split ... (FLAT, enables <step>)`), then the step, each after its own four checks
(the guard `--together` for the split). If the step scores `UP` only against the split's own score
and not against the score before the split, revert both. The split moves members only and widens
nothing, so this exception adds no behaviour risk. *Primitive Obsession* counts as a file-level smell
here, like *Overall Code Complexity*.

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

## Choosing the next step

Work the task file's steps in order, with one exception. When a file is close to a file-level
smell (*Overall Code Complexity*, *Number of Functions in a Single Module*), each new method
uses up room that later steps need. Then measure the candidate applications on their own
first: apply one, run `codehealth_check.py`, revert, and so on for each. Commit the one with
the highest `UP` score, after all four checks. Say in your report that you chose this way.

A method that stops being a *Complex Method* can reveal *Large Method*, which was hidden while
the first smell was reported (R2-04 `GetVideoDirectPlayProfile`); a step that does so is `WORSE`
by the usual rule. Do not retry it; report the method as blocked until its line count falls.

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
| 2026-10-17 | Choosing the next step: near a file-level smell, measure the candidate applications alone and commit the best `UP` one, instead of strict task order. | R2-11 `ProbeResultNormalizer.Itunes`: the file had room for one more method before *Overall Code Complexity*; step 1 used it for +0.02 (7.98 to 8.00), while step 4 alone measured 8.18. Every later step was then DOWN. R2-02 likewise found any 3 of 17 extractions, never 4, could score UP. Made under the campaign owner's mandate of 2026-10-17 to evolve the playbook; not individually reviewed. |
| 2026-10-17 | `codehealth_check.py`: a step that adds a parameter named with `@` (verbatim identifier) is `WORSE`, because CodeScene skips such methods and the score cannot be trusted. | R2-10 `DynamicHlsController.Video`: extracting a DTO initializer into a 53-argument helper with `bool? @static` and `string? @params` measured UP (7.98 to 8.52) because the method was missing from the review; renamed on a scratch copy it measured 8.01 with the helper flagged. The agent caught it and reverted. No landed step has such a parameter (checked over the whole campaign diff). |
| 2026-10-17 | Recipe E rule 7 generalised: a tail of the method whose every path ends in `return` or `throw` (a loop plus the final return, a chain of `if (...) { return X; }`, a block that returns on every path) may be extracted as one method called as `return M(...)`; a variable the tail assigns before reading it is declared inside the new method with its original type. Rule 9: the extracted block need not be nested; a run of statements forming one step is enough. | Trailing `if` chains blocked work in R07 `GetPlaylistAudioCodecs`, R2-05 `GetOutputFileExtension`, R2-08 `GetPathProtocol`, R2-09 `GetPlaylistAudioCodecs`, R2-03 `RegisterItem`, R2-04 `GetOptimalAudioStream` and `GetAudioDirectPlayProfile`. R2-01 already extracted return-on-every-path blocks (`GetAudioVbrModeParam`, `GetBitStreamArgs`, `GetSwScaleFilter`, `GetVideoBitrateParam`, `GetHwTonemapFilter`, where `args = ...` became a declaration) and asked for a ruling. R2-04 and R2-03 (`LogDirectPlayEligibility`, `DeleteItemPath`) extracted single statements that carried `??` complexity. Adopted by the campaign owner. |
| 2026-10-17 | Recipe P: the expression need not be built from `&&` and `||`; a boolean that carries its complexity in `??`, `?:` or `?.` is a predicate. | R2-04 `IsAudioBitrateLimitExceeded` wraps `IsBitrateLimitExceeded(item, options.GetMaxBitrate(true) ?? 0)`; it scored UP. Adopted by the campaign owner. |
| 2026-10-17 | Nullable flow analysis, case 3: for a nullable-valued property null-checked inside the expression, the guard terms of the object stay first at the call site and the property is passed as a `[NotNullWhen]` parameter. Compile-tested in a scratch project: it builds with warnings as errors, and without the attribute fails with CS8629. | R2-09 `GetOutputVideoCodecLevel` (`state.VideoStream.Level`) and `GetOutputAv1BitDepth` (`state.VideoStream.BitDepth`); R2-03 `UpdateDateBasedEpisodeNumbers` (`episode.Year`, `Month`, `Day`). Adopted by the campaign owner. |
| 2026-10-17 | Recipe S: also when *Overall Code Complexity* blocks every new method; at most two groupings, and a file with no grouping that scores `UP` is reported as blocked. | R2-11 `ProbeResultNormalizer.Itunes`: the file had room for one more method, then every E or P step scored DOWN. R2-02 `ApplyTranscodingConditions`: a fourth extraction brings three smells at once. R2-03: every grouping that clears *Number of Functions* brought *Low Cohesion*. Adopted by the campaign owner. |
| 2026-10-17 | Recipe D: blocks or loops that differ in a single constant or enum member may be merged into one helper taking that value, even when CodeScene reports no *Code Duplication*. | R2-11 `ProcessPairs`: three `foreach` loops that differ only in `PersonKind`. Adopted by the campaign owner. |
| 2026-10-17 | Choosing the next step: a step that reveals *Large Method* is `WORSE`; do not retry, report the method as blocked. | R2-04 `GetVideoDirectPlayProfile`: reducing it from a Complex Method showed Large Method (81 lines), 7.79 to 8.29 was WORSE. |
| 2026-10-17 | Rule 1: a single recipe may be applied the same way to several methods of one file in one commit when each application alone is `FLAT` and together they score `UP`, as Recipe D already could. | R3-22 `DynamicHlsHelper.Codecs`: nullable case 3 on `GetOutputVideoCodecLevel` and on `GetOutputAv1BitDepth` was `FLAT` each (the guard terms stay behind), `UP` together (8.54 to 9.09); the agent did not commit it because the rule allowed multi-method commits only for Recipe D. Same shape as R17 `EncodingJobInfo` (one of three methods per commit FLAT, all three UP). Made under the campaign owner's mandate of 2026-10-17 to evolve the playbook; not individually reviewed. |
| 2026-10-17 | Recipe S: a split that scores `FLAT` (never `DOWN`) is kept as a pair with the step it enables, when the pair scores `UP` against the score before the split; *Primitive Obsession* counts as a file-level smell for this. | R3-33 `StreamBuilder.VideoStream`: a split of small methods scored FLAT (8.67), and then six extractions that were DOWN before (7.70 to 7.73) scored UP (8.72 to 8.75). R3-27 `ProbeResultNormalizer`: a second split FLAT (8.54), with the predicate step on top 8.79 UP. Both agents correctly did not commit the pair under the old wording. Made under the campaign owner's mandate of 2026-10-17 to evolve the playbook; not individually reviewed. |
| 2026-10-17 | Recipe D: an identical copy may call a helper that lives in another part of the same partial class, when the helper is not widened and its body is identical to the copy. | R3-05 `EncodingHelper.IntelFilters`: `GetIntelVidFilterChain` called `IsQsvOclSupported`, `IsIntelDx11OclSupported` and `IsIntelVaapiOclSupported` from `EncodingHelper.HwDecoders.cs` (bodies identical, checked); R2-01 `EncodingHelper` did the same across its split files and asked for a ruling. Made under the campaign owner's mandate of 2026-10-17 to evolve the playbook; not individually reviewed. |
| 2026-10-18 | Recipe P: the predicate may take a leading run of the terms of an `||` or `&&` chain, the remaining terms staying at the call site in order. | R3-03 `EncodingHelper.VideoEncoderParams` `IsGopOnlyKeyFrameEncoder`: the first seven of eleven `||` terms became `IsGopOnlyH264OrHevcKeyFrameEncoder` (+0.17); the agent asked for a ruling. Nullable case 3 already used the same shape for `&&` chains. Made under the campaign owner's mandate of 2026-10-17 to evolve the playbook; not individually reviewed. |
| 2026-10-18 | Recipe E rule 10: a block inside a loop that ends in `continue` on some paths moves to a `bool` method (`continue` becomes `return true`, falling out becomes `return false`, the call site is `if (Helper(...)) { continue; }`). | Blocked `ApplyTranscodingConditions` (10 case guards), `ProbeResultNormalizer.Itunes` (`if (reader.IsEmptyElement) { reader.Read(); continue; }` in three methods; R3-19). Blocks that mix `continue` with `return` (`GetExternalSubtitleProfile`) are not covered. Compile-tested and compared with the original on 20,000 random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 11: a suffix of the arms of a large `switch` expression, with the discard arm, moves to a helper; the original gets `_ => Helper(x)`. | Mapping switches blocked `DownMixAlgorithmsHelper.GuessLayoutFromChannelCount` (cc 11), `GetTranscodeReasonForFailedCondition` (cc 28), `GetExtraTypeNameKey` (cc 14), `HlsCodecStringHelpers` level and profile switches (cc 13 each), `AppendVaapiCompressionLevel`, `AppendSvtAv1Preset`. Compared with the original on 20,000 random inputs, including a `when` guard and a `throw` arm. Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 12: a private readonly record struct carries the shared read-only inputs of a block that needs more than four values; the moved code reads `context.X`. | The sw-decoder, hw-scale and subtitle blocks of `GetIntelQsvDx11VidFiltersPrefered`, `GetIntelQsvVaapiVidFiltersPrefered`, `GetAmdDx11VidFiltersPrefered`, `GetNvidiaVidFiltersPrefered`, `GetAppleVidFiltersPreferred`, `GetRkmppVidFiltersPrefered` and the three Vaapi methods need 8 to 17 shared values; every extraction was WORSE on Excess Number of Function Arguments (R3-05, R3-06, R3-31, R3-37, R3-38, R3-40; R2-01). Agents also asked for it (R3-05, R3-37, R3-40). Compiled under warnings as errors and compared with the original on random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe P: an `out` variable assigned only on some paths is allowed, the predicate starting with `x = default;` for each such parameter; and a predicate may be a local function when the expression calls a local function. | CS0177 blocked `GetAspectRatio` and `IsNearSquarePixelSar` (R3-24), `FetchWtvInfo` (R04), `GetH26xOrAv1Encoder` and `GetMjpegEncoder` (R01); a local-function predicate scored 8.26 UP on `PrepareExtra` (R3-17) but was held back. Both compiled under warnings as errors and behaved identically on random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe D: copies that differ in two values may be merged into one helper of at most four parameters. | `PathManager.GetAttachmentFolderPath` and `GetSubtitleFolderPath` differ in the cache base path and a log-message literal (R3-52). Adopted by the campaign owner. |
| 2026-10-18 | Rule 1: a pair of steps of two different recipes in one file, each alone FLAT and together UP, may be one commit, naming both recipes. | R3-04 `EncodingHelper`: a predicate on the `preventHlsAudioCopy` initializer (FLAT) plus an extraction in `GetInputModifier` (FLAT) scored 7.55 together (from 7.32). Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 10: the block may contain a `switch` statement of its own (its `break`s stay, each `continue;` in it becomes `return true;`). | Blocked `ReadFromDictNode`, `ReadPlistRoot`, `ReadValueArray` (R4-33: four steps all FLAT, deep nesting stayed) and the case guards of `ApplyTranscodingConditions` (R4-02: the fourth guard made `ApplyTranscodingConditions` Complex Method cc 66, WORSE). Compiled under warnings as errors and compared with the original on 20,000 random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 11 also covers a `switch` statement whose sections return: a suffix of the sections moves with `default:`. | `GetTranscodeReasonForFailedCondition` (R4-23: probe DOWN 8.94 to 8.87, both halves cc 15-16, so the split point must be measured), `IsVideoConditionSatisfied` (R4-07, cc 21, 19 sections), `ApplyTranscodingConditions` (R4-02, cc 66). Compiled and compared on 20,000 random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 12: up to two context structs per method, one per family of blocks; the whole statement that holds the block moves, guard included. | One struct per method was full at 12 members in every EncodingHelper filter method (R4-09, R4-35, R4-36, R4-05, R4-19), leaving the decoder blocks (12 to 17 values) blocked; moving the whole `if (hasSubs)` statement instead of its inside is what removed the bumps (R4-09, R4-35, R4-36). Compiled and compared on 20,000 random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe E rule 13: the tail of an `if` / `else if` chain moves to a helper (`else { x = Helper(...); }` or `else { return Helper(...); }`). | Used already by R4-03 under rules 7 and 9 on `GetEncoderParam`, `AppendLevelParam` and `NormalizeTranscodingLevel` (6.85 to 8.03, Bumpy Road gone); also the shape of `GetHwDeviceAndDecoderArgs` (R4-06, still six bumps). Compiled and compared on 20,000 random inputs. Adopted by the campaign owner. |
| 2026-10-18 | Recipe D: a logging message template is never a value moved into a helper parameter (CA2254). | R4-48 `PathManager`: the two-value merge adopted earlier the same day fails the build with CA2254, so no step was possible. Correction of the earlier row. Made under the campaign owner's mandate. |
| 2026-10-19 | Recipe E rule 11 also covers a `switch` statement whose sections end in `break` (no `return`): a suffix of the sections, with `default:`, moves to a helper switch, each section keeping its `break`. | Measured in round 5: `ApplyParam` (R4-18, 34 sections) and `ApplyTranscodingConditions` (R4-02) are void switches of this shape. Compiled with warnings as errors and compared with the original on 20,000 random inputs (0 failures). Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-19 | Recipe E rule 13 also covers a void tail: branches that only add to or change objects passed in (no assigned local read afterwards) move to a void helper, called from `else { Helper(...); }`. | Round 5: the Amd vaapi branch of `EncodingHelper.VaapiFilters` (R4-05, 7.86 UP as a void tail, not committed under the old rule). Compiled with warnings as errors and compared with the original on 20,000 random inputs (0 failures). Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 12: a helper may take both structs of its method; the guard may stay at the call site when the whole statement needs more than fifteen values; a struct construction is written on one line. | Round 5: the Amd and Intel decoder chains of `EncodingHelper.VaapiFilters` (R4-05: 7.95 and 7.90 UP, but the helper read both structs), the sw-decoder and hw-scale blocks of `EncodingHelper.RkmppFilters` (R4-19: 9.12 and 9.13 UP, whole statements need 16 values), the Intel software-decoder block (R4-09: 16 values), and the layout finding of R4-36. Parameter plumbing only: no statement changes. Not adopted: `??` defaults in Recipe P (no run gained), a larger member cap. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 12: one struct per family of blocks, at most three per method (it was two). | Made AFTER the fact: two commits already landed broke the old wording, which the coordinator missed when landing them: `GetNvidiaVidFiltersPrefered` (R6-41) and `GetRkmppVidFiltersPrefered` (R6-29) each build three structs, one per family (subtitles, software decoder, hardware decoder). A third held candidate (R6-42, `GetAmdDx11VidFiltersPrefered`, 9.52 to 9.56) has the same shape. Each struct still serves one family and is constructed once; the change is parameter plumbing. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe D: the two branches of an `if` / `else` that differ only in one value become one body after choosing the value with `?:`. | Found in `PathExtensions.AttributeValueSearch` (the `shortAttr` / `attribute` branches; a `ref`/`out` helper would need five parameters, which CodeScene flags). Measured there (R6-44): FLAT, because the file's smell is cyclomatic complexity and the `?:` counts like the `if`; the recipe text was corrected the same day to say it helps only where it removes nesting. Compiled under warnings as errors (string and `ReadOnlySpan<char>` forms) and compared with the original on 20,000 random inputs: 0 failures. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 14: a parameter object for a private method (a contiguous run of its parameters, in order, becomes a private readonly record struct; call sites build it in place). | 31 of the 61 scope files below 10.00 carry Excess Number of Function Arguments; where the method is private (all of `StreamBuilder.cs`'s, `StreamBuilder.TranscodingConditions`, `EncodingHelper.StreamCopy`, `StreamBuilder.VideoStream`, `UserViewManager`, the helper files), no other recipe can remove it. Compiled under warnings as errors and compared with the original on 20,000 random inputs, including a trace of the order in which call-site arguments are evaluated: 0 failures. Public and protected signatures stay frozen. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 14 extended: overloads of the same name are allowed; a parameter the method assigns is first copied into a local; at most two runs per method. | `UserViewManager.GetUserView` (a public overload of the same name; the private step scored 9.68 UP on a scratch copy) and `StreamBuilder.VideoStream.BuildStreamVideoItem` (9 parameters, `audioStream` assigned inside). Compiled under warnings as errors and compared with the original on 20,000 random inputs, with two runs, an assigned parameter, a public and a private overload of the same name, and an evaluation-order trace: 0 failures. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe S: a file may be split into up to four new files (it was at most two groupings), when the original and every new file reach 10.00 and the methods move unchanged. | Made AFTER the fact, like the rule 12 change the same day: a split agent working toward the campaign owner's goal of 10.00 for every scope file split `EncodingHelper.HwSupport`, `AudioParamHelpers`, `Bitrate` and `ProbeResultNormalizer.MediaStream`, `Video` into 13 new files (two to four each); every one- or two-file grouping it measured left the original or a new file below 10.00. All 18 files now score 10.00, the guard passes with `--together`, and no method body changed. The cost is more files; the backlog row lists them. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 11, mixed sections: a switch statement directly followed by `return v;` may have sections ending in `return` or `break`; a suffix of whole sections (or a single section, its `break` becoming the helper's final `return v;`) moves to a helper ending in the same `return v;`. | Proposed by the agent on `StreamBuilder.TranscodingCases.TranscodingCaseIsSkipped` (cc 44 to 27 with per-section helpers, a suffix split measured 9.63 UP); the per-section form was landed the same day as rules 7/9 after the coordinator checked that only `return false;` follows the switch. Compiled under warnings as errors and compared with the original on 20,000 random inputs: 0 failures. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 15: a block that writes two or three locals read later moves to a method that takes and returns their values in a private readonly record struct (an exception to step 4's "no result class"). | The most common remaining blocker: `JobLogger.ParseLogLine` (percent, transcodingPosition; measured 10.00 in this form, 9.68 DOWN with plain parameters), `UserDataManager.UpdatePlayState` (whose round-5 tuple return this replaces in spirit), `PathExtensions.AttributeValueSearch` (attributeIndex, attributeEnd). Compiled under warnings as errors and compared with the original on 20,000 random inputs: 0 failures. Made under the campaign owner's mandate, not individually reviewed. |
| 2026-10-21 | Recipe E rule 15 in a loop: the whole loop body may hold `continue` (each becomes a return of the current values), or a block may carry a `Skipped` flag in its result (each `continue` returns the flag set; the call site continues after assigning the locals). Rule 9: a block that calls a local function becomes a local function. | `ProbeResultNormalizer.Itunes.ReadFromDictNode` (9.09 to 10.00 on a scratch copy), `PathExtensions.AttributeValueSearch` (10.00 on a scratch copy with the flag form), `LibraryManager.Extras.FindExtraCandidates` (9.38 to 10.00 on a scratch copy with a local function). Both loop forms compiled under warnings as errors and compared with the original on 20,000 random inputs: 0 failures. Made under the campaign owner's mandate, not individually reviewed. |
