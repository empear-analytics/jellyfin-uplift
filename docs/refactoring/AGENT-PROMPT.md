# Instructions for a task agent

This file is read by every agent that works one backlog task. The message that started you names
your task, your worktree and anything specific to the file. Everything below applies to you.

## Where you work

- You work in the git worktree named in your message, on its own branch (`task/<id>`). Work only
  inside that folder, commit on that branch, and do not push, switch branches or touch any other
  folder. Use the shell (Bash) for every command, and run each command from inside the worktree.
- Never use `git stash`.
- **Leave the machine as you found it** (see AGENTS.md): never run `dotnet build-server shutdown`,
  leave no process running (also no background command that waits for input), and keep temporary
  scripts and files in a folder of your own under the system temp directory, named after your task.
  Delete it before you report.
- Never modify anything under `tests/`.

## What to read, in this order

1. `AGENTS.md`
2. `docs/refactoring/PLAYBOOK.md`, in full, including the "Changes to this playbook" table. It
   changes between rounds; read the current text, not what you remember.
3. Your task file (`docs/refactoring/tasks/<id>-*.md`).

## How you work

- Follow the task file and the playbook exactly. The playbook is a closed catalogue: a change that
  is not one of its recipes exactly as written (a lookup table, a type other than the context struct
  of Recipe E rule 12, merging nested `if`s into one condition, a change to a signature) is not yours
  to make. Report it as a candidate. The recipes of 2026-10-18 are in force: rules 10 (a block that
  ends in `continue`), 11 (a suffix of a `switch` expression's arms) and 12 (a context struct) of
  Recipe E, `x = default;` for `out` parameters and local-function predicates in Recipe P, Recipe D
  for two differing values, and a pair of FLAT steps of two recipes in one commit.
- Before a step that adds methods, follow "Choosing the next step": measure the candidate
  applications on their own first and commit the best `UP` one.
- After every step, before committing it, run all four checks from section 7 of the task file.
  When a step uses Recipe D, run the guard with `--dedup`; when it uses Recipe S, with
  `--together` over the original and the new files (both flags when both apply).
- Keep a step only on the verdict `UP`. On `WORSE`, `FLAT` or `DOWN`, or if any check fails,
  revert it. A step that adds a parameter named with `@` (such as `bool? @static`) is `WORSE`
  whatever the tool says, because CodeScene skips such methods.
- A new private method must not reuse a name that already exists anywhere in the class. If your
  file is one part of a partial class, search all of its parts first. Other agents may be working
  on other parts at the same time.
- When a test fails, write down the full name of the failing test before you do anything else.
  Then follow the procedure in AGENTS.md.
- Stop and report if anything is unclear. An unfinished task is fine.

## When to stop

Stop after at most 25 commits, or when no step scores `UP` any more, whichever comes first.

Before you report, run the full solution test suite once (`dotnet test Jellyfin.sln --no-build`)
after a `dotnet build Jellyfin.sln --no-incremental`, and give the result.

## What to report

Finish with the report block at the end of the task file, filled in. If you split the file, give
the score of the original and of each new file. For each smell still present when you stop, give
one line: whether you tried it and its verdict, or why no recipe applies. List candidates for new
recipes separately, with the file and method where each came up.
