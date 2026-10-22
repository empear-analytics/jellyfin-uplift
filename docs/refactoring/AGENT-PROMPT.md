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
  of Recipe E rule 12, merging nested `if`s into one condition, a change to a signature other than rule 14's
  parameter object for a private method) is not yours
  to make. Report it as a candidate. The recipes of 2026-10-18 are in force: rules 10 (a block that
  ends in `continue`), 11 (a suffix of a `switch` expression's arms) and 12 (a context struct) of
  Recipe E, `x = default;` for `out` parameters and local-function predicates in Recipe P, Recipe D
  for two differing values (never a log template: CA2254), and a pair of FLAT steps of two recipes
  in one commit. The additions of the evening of 2026-10-18 are in force too: rule 10 with a
  `switch` inside the block, rule 11 for a `switch` statement, up to two context structs per method
  in rule 12, and rule 13 (the tail of an `if` / `else if` chain).
  Added on 2026-10-19: rule 11 for a `switch` statement whose sections end in `break` (a void switch), and
  rule 13 with a void tail. The measuring scripts (`codehealth_check.py`) now go through the CodeScene MCP
  server by themselves and fall back to the CLI; you change nothing, but if the server throttles
  ("Access token is invalid or expired" although the token is fine) the script waits and retries, which
  can take a minute or two. Do not delete or restart the server.
  Added on 2026-10-21 (rule 12): a helper may take both structs of its method; the guard may stay at the
  call site when the whole statement needs more than fifteen values; write a struct construction on one line.
  Later on 2026-10-21: one struct per family of blocks, at most three per method; and Recipe D for the two
  branches of an `if` / `else` that differ only in one value (choose the value with `?:`, keep one body).
  Also on 2026-10-21: rule 14 of Recipe E, a parameter object for a PRIVATE method with too many parameters
  (read its conditions exactly; public, protected and internal signatures stay frozen).
  Later the same day: rule 11 for a switch whose sections mix `return` and `break` (followed by `return v;`),
  and rule 15, a result struct for a block that writes two or three locals read later.
  Also added on 2026-10-21: rule 16, an exit helper for a block whose paths either `return` or fall
  through (void `return;`, one constant, or any expression with a `(Returned, Value)` struct as the last
  statement of a loop body); and in rule 14, a struct shared by two methods with identical runs.
  Later on 2026-10-21 (the owner's goal: every smell gone that needs no public API change): rule 14
  for Primitive Obsession / String Heavy Function Arguments (runs with a primitive in private methods of
  any parameter count, applied as one group per commit); rule 12 with up to four structs; rule 15 with a
  `Stopped` flag for `break`; rule 17 (stacked case labels to an `is A or B` predicate); and Recipe P
  with a pattern variable as a `[NotNullWhen(true)] out` parameter.
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
