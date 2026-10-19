# CLAUDE.md

**Read [`AGENTS.md`](AGENTS.md) first.** It is the authoritative contract for all agents
working in this repository, and everything in it applies to you. This file only adds what
is specific to Claude Code.

## Claude Code specifics

**Measure through the CodeScene MCP server, with the scripts.** `python tools/codehealth_check.py <file>`
and `python tools/codehealth_sweep.py` review each file through the CodeScene MCP server (so the
measurement is recorded on the CodeScene side) and keep the verdict logic of the campaign. They
fall back to the CodeScene CLI for a file, or for the whole run, when the server is unavailable.
Do not call the server by hand for the per-commit check: you would lose the UP / FLAT / WORSE
verdict. (An earlier note said the server returns nothing for files that take more than five
seconds, such as `EncodingHelper.cs`. That is true only for a one-shot call that closes the
input at once; the scripts keep it open, and the scores are identical to the CLI's.)

The server is started from `.mcp.json`; the scripts read its `codescene` entry. `.mcp.json`
is machine-specific and intentionally not committed, and a session loads the `.mcp.json` of its
own root folder.

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
