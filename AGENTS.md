# Agent instructions

Read this before changing anything. It applies to every agent and every harness.

## What this repository is

Jellyfin is a **free software media server** written in C#/.NET. It scans media libraries,
probes files with ffprobe, decides per client whether to play a file directly or transcode
it, and builds the ffmpeg command lines and HLS playlists that do so. Many clients depend
on its HTTP API, and plugins build on the public API of its packages.

The practical consequence:

> **Behaviour fidelity outranks code quality, readability, and your own judgement.**
> A change that makes the code cleaner but picks a different audio track, or changes one
> character of an ffmpeg command line, is a defect, even if it looks obviously correct.

Code that appears wrong may be relied on by a client or a plugin. **Report it, do not fix it.**

## Build and test

```bash
dotnet build Jellyfin.sln
dotnet test Jellyfin.sln --no-build
```

- A cold build takes about three minutes; an incremental build after a one-file change
  under a minute. The base has **0 errors and 211 warnings** (`--no-incremental`); keep it so.
- The full test suite takes about three minutes. Each task file names the test projects
  that cover its part; run those after every step and the full suite before you finish.
- Some analyzer errors appear only when the whole solution builds. Always build
  `Jellyfin.sln`, not a single project.
- Never commit a broken build or a failing test.

## Behavioural verification

The test suite includes acceptance tests under `tests/*/Acceptance/` that record the current
behaviour at stable boundaries (the public API of the plugin packages, and HTTP), with
separate recordings for Windows and Linux. They are the best check that behaviour was
preserved - but they catch only about half of all behaviour changes. See
[`docs/refactoring/test-harness.md`](docs/refactoring/test-harness.md).

**Never modify anything under `tests/`.** A failing test means your change altered
behaviour: revert the change, never the test.

## The refactoring campaign

If you were given a task ID like `R26`, your instructions are in
[`docs/refactoring/tasks/`](docs/refactoring/tasks/). Read, in order:

1. Your task file, e.g. `docs/refactoring/tasks/R26-HlsHelpers.md`
2. [`docs/refactoring/PLAYBOOK.md`](docs/refactoring/PLAYBOOK.md) - the closed catalogue of allowed changes
3. [`docs/refactoring/README.md`](docs/refactoring/README.md) - campaign rules

You may apply **only** the five recipes in the playbook. Anything else is out of scope.
`docs/experiment/` is not part of the campaign: do not read it.

## Hard prohibitions

Never, in any commit:

- Change a numeric, string, or character literal, or an enum member value.
- Change arithmetic, bitwise operations, or shifts.
- Change a comparison operator, including `<` to `<=`, except for the exact complement a
  guard clause needs.
- Change a type, including width (`int` to `long`) or nullability.
- Reorder statements that have side effects.
- Delete code that looks dead - it may be reached through dependency injection, reflection,
  a plugin, or HTTP.
- Modify a static data table.
- Rename, remove, or change the signature or accessibility of a member that is not `private`.
- Modify anything under `tests/`.
- Fix a bug you noticed. Report it.

## Verification sequence

Run all four after **every** step, **before you commit it**, in this order. Both tools compare
your working tree with `HEAD`; run after the commit, they would compare the commit with itself
(add `--base HEAD~1` if you need to re-check a commit you already made):

```bash
# 1. it still builds
dotnet build Jellyfin.sln

# 2. the tests that cover this part still pass (your task file lists them)
dotnet test tests/<project> --no-build

# 3. no constant was removed or altered
python tools/refactor_guard.py <the file you changed>

# 4. the metric improved
python tools/codehealth_check.py <the file you changed>
```

`refactor_guard.py` extracts every literal before and after your change and compares them.
A **removed** literal fails: it means a constant was altered or code carrying it was
deleted. Added literals are only warnings - a new guard clause can legitimately bring its
own value.

If the tests or the guard fail, revert immediately:

```bash
git checkout -- <file>
```

Two recipes move or remove literals by design. Recipe D (deduplicate) removes copies, so
check it with `--dedup`: a literal may occur fewer times, as long as it still occurs and no
new value appears. Recipe S (split file) moves code to new files, so check the original and
the new files as one unit with `--together`.

**Never use `git stash`.** The stash is shared by every worktree of this repository, so
another agent's `git stash pop` can take your change, and yours can take theirs. To check
whether a failing test also fails without your change, commit your change first, then put
the start commit's version of the file back, rebuild and rerun, and restore your version:

```bash
git commit -am "refactor(<File>): simplify <Method>"
git checkout <start commit> -- <file>
dotnet build Jellyfin.sln && dotnet test tests/<project> --no-build
git checkout HEAD -- <file>
```

Then revert your commit either way (`git revert --no-edit HEAD`): if the test passes without
your change, your change broke it; if it fails there too, your change cannot be verified.
In the second case, stop and report.

## Commits

- One recipe, one method, one commit. One commit may apply the same recipe several times
  within one method; Recipe D across several methods is one commit (see the playbook).
- Message format: `refactor(<File>): simplify <Method>`
- Never squash several methods into one commit. The campaign relies on being able to
  revert a single step.

## When to stop

Stop and report, rather than pressing on, whenever:

- The baseline score in your task file does not match what you measure.
- The build fails and the fix is not obvious.
- A test fails and you do not understand why.
- A test fails on the start commit as well, without your change.
- The guard fails and you do not understand why.
- A method's control flow is too tangled to transform confidently.
- You want to make a change the playbook does not cover.

**An unfinished task is an acceptable outcome. A silently broken media server is not.**
Reporting "I completed 2 of 5 steps and stopped because step 3 was unclear" is a good
result.
