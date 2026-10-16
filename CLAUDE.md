# CLAUDE.md

**Read [`AGENTS.md`](AGENTS.md) first.** It is the authoritative contract for all agents
working in this repository, and everything in it applies to you. This file only adds what
is specific to Claude Code.

## Claude Code specifics

**Measure with the scripts, not the MCP server.** `python tools/codehealth_check.py <file>`
runs the CodeScene CLI directly. The CodeScene MCP server, if you have it, drops answers
that take longer than five seconds; `EncodingHelper.cs` takes about seven, so its score
comes back empty without an error.

`.mcp.json`, if present, is machine-specific and intentionally not committed.

## Refactoring campaign

Task files live in [`docs/refactoring/tasks/`](docs/refactoring/tasks/). They name the
commands directly, so you can execute them as written.

You are the right harness for the tasks that need judgement - Recipe E (extract method)
and Recipe S (split file), where the agent must reason about which variables cross the
boundary, `await`, and what stays `private`. See
[`docs/refactoring/AGENT-SETUP.md`](docs/refactoring/AGENT-SETUP.md) for how work is
routed across harnesses.

## Reminders that matter here

- Run `python tools/refactor_guard.py <file>` after every step, before committing. It is cheap and it
  catches the one class of mistake that no build error will.
- Build the whole solution: some analyzer errors only appear in a full build.
- Never touch `tests/`. A failing test means revert your change.
- R20, R23 and R28 are barely covered by the tests (mutation score 0%). Work them like the
  others, apply the recipes exactly, and say in the report that the changes are unverified.
- Stop at a Code Health score of 10.00, or when every remaining smell has no recipe.
