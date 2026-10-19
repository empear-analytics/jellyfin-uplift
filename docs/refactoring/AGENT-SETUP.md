# Agent setup and work routing

How to point each harness at this campaign, how to hand out a task, and which agent should
get which kind of work.

Three harnesses are planned:

| # | Harness | Instruction file it reads |
| --- | --- | --- |
| 1 | Claude Code | `CLAUDE.md` (plus `AGENTS.md`) |
| 2 | Codex | `AGENTS.md` |
| 3 | OpenCode | `AGENTS.md` |

`AGENTS.md` at the repo root is the shared contract. Everything harness-specific lives
here or in `CLAUDE.md`.

---

## What every harness needs

The checks run from the shell. They measure through the CodeScene MCP server, which they start
themselves from the `codescene` entry of `.mcp.json` (`tools/cs_mcp_client.py`), so no harness has to
provide an MCP server:

| Need | How |
| --- | --- |
| .NET SDK 10 | `dotnet --version` reports 10.0.x (`global.json` pins the major version) |
| Python 3.7 or later | for the tools in `tools/` (`python` on this machine is 3.7; that works) |
| `.mcp.json` and Node | the `codescene` entry names the server (`node .../codehealth-mcp/bin/cs-mcp.js`); `tools/cs_mcp_client.py` reads it |
| The CodeScene CLI (fallback) | `tools/codehealth_check.py` and `codehealth_sweep.py` use it when the server cannot be reached or gives no review for a file; they find the `cs` binary bundled with the CodeScene MCP server (`%LOCALAPPDATA%/codehealth-mcp/MCP-<version>/cs.exe`); set `CS_CLI` to point elsewhere |
| `CS_ACCESS_TOKEN` | in the environment, for the CLI |

Check the setup with:

```bash
python tools/codehealth_check.py Jellyfin.Api/Helpers/HlsHelpers.cs
```

It should print a score of 9.38 at `HEAD` and now, and the verdict `FLAT`.

The scripts call the MCP server's `code_health_review`. An earlier version of this file said not to use
the server for `EncodingHelper.cs`, because the server drops an answer still in flight when its input
closes after five seconds. That holds for a one-shot call; the scripts keep the input open for the whole
run and get the same scores as the CLI (checked on 37 files of rounds 4 and 5, `EncodingHelper.cs` among
them). `--backend cli` forces the CLI and `--backend mcp` forbids the fallback.

---

## Routing work by recipe, not by file

The recipes in [`PLAYBOOK.md`](PLAYBOOK.md) differ sharply in how much reasoning they need.
Route on that, not on file size:

| Recipe | What the agent must reason about | Needs |
| --- | --- | --- |
| **P** - named predicate | Nothing. Copy an expression into a method, character for character. | Any agent |
| **G** - guard clauses | What the method returns when it falls through, and the exact complement of nullable comparisons. | Mid capability |
| **E** - extract method | Which variables cross the boundary, which are written, and `await`. Data flow. | Strongest agent |
| **D** - deduplicate | Whether two blocks are truly identical. Checked with `refactor_guard.py --dedup`. | Strongest agent |
| **S** - split file | Partial classes, what stays `private`, analyzer rules. Checked with `--together`. | Strongest agent |

A useful rule: **Recipe P is safe to hand to the smallest model you have.** It is a
copy-paste transformation with a mechanical checker behind it. Recipe E is where a weaker
model will silently drop a variable - and where the build, and possibly the tests, will
still pass.

### Suggested starting split

Do not treat this as settled - calibrate first (next section).

- **Claude Code** - Recipe E and S. R01 `EncodingHelper.cs` (8,000 lines, scored 1.00, 70+
  flagged methods) is the hardest unit in the campaign.
- **Codex** - Recipe G and E on mid-difficulty files: R06 `StreamingHelpers.cs`, R07
  `DynamicHlsHelper.cs`.
- **OpenCode / smaller model** - Recipe P only, across any file. There are 193
  complex-conditional findings to work through, and each one is independently verifiable.

---

## Calibrate before you commit to a split

The table above is a hypothesis. None of these model/harness pairings has run against this
codebase, and model capability is not something to assume.

**Run the same probe on all three, then compare:**

1. Give each agent **R32 `FFProbeHelpers.cs`** and **R30 `TranscodingJob.cs`**: small files,
   one Recipe P step each, well covered by the tests (mutation score 69% and 58%).
2. Restrict every agent to **Recipe P only**.
3. Compare on four axes:
   - Did the score improve, and by how much?
   - Did the tests and `refactor_guard.py` pass?
   - Did it stop when told to, or keep going past the target?
   - Did it stay inside the playbook, or invent a transformation?

Give each a clean branch so the runs do not interfere:

```bash
git checkout -b probe/p-<agent-name> refactoring/campaign
```

The fourth axis matters most. An agent that improves the score while quietly making changes
the playbook forbids is worse than one that stops early, because the tests catch only about
half of all behaviour changes.

Record what you find in [`BACKLOG.md`](BACKLOG.md) and route the real work accordingly.

---

## Dispatching a task

Give the agent exactly this, with the task ID substituted:

```
Work task R32 in this repository.

Read, in this order:
  1. AGENTS.md
  2. docs/refactoring/PLAYBOOK.md
  3. docs/refactoring/tasks/R32-FFProbeHelpers.md

Follow the task file exactly. Apply only Recipe P.

After every step, before committing it, run all four checks from section 7 of the task file:
  dotnet build Jellyfin.sln
  dotnet test tests/Jellyfin.MediaEncoding.Tests --no-build
  python tools/refactor_guard.py MediaBrowser.MediaEncoding/Probing/FFProbeHelpers.cs
  python tools/codehealth_check.py MediaBrowser.MediaEncoding/Probing/FFProbeHelpers.cs

If a check fails, revert that commit and move on.
Never modify anything under tests/.
Stop and report if anything is unclear. An unfinished task is fine.

Finish with the report block at the end of the task file, filled in.
```

Keep the "stop and report" line. Weaker models will otherwise improvise once they run out
of instructions, and improvisation is the failure mode that matters here.

---

## Reviewing agent work

Before merging any campaign branch, the coordinating agent checks:

1. `python tools/refactor_guard.py --all --base <start commit>` - no removed literals
   (`--together` for split files, `--dedup` for deduplicated ones).
2. `git log --oneline <start commit>..` - one method per commit, correct message format.
3. `git diff --stat <start commit> -- tests` - empty.
4. `git diff <start commit> -- <file>` - scan for anything outside the playbook. Particularly:
   changed comparison operators, changed types, deleted branches, widened accessibility.
5. `dotnet build Jellyfin.sln --no-incremental` and `dotnet test Jellyfin.sln --no-build`.
6. Confirm the reported final score with a fresh `python tools/codehealth_check.py <file>`.

Do not skip step 4 because the others passed. The guard checks constants, not control flow,
and the tests miss about half of all behaviour changes - an inverted condition with no
literal change can pass every automated check.
