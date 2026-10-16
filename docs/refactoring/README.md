# Code Health Refactoring Campaign

A structured, measurable campaign to bring one bounded part of Jellyfin to Code Health
10.00, designed so that the work can be distributed across many agents - including agents
that are not capable enough to reason about the whole system.

Every task is scoped to **one file**, bounded by a **closed catalogue of transformations**,
and judged by an **external metric** (CodeScene Code Health) rather than by taste.

The campaign follows the one that founded the 3SX campaign (Street Fighter III, 3sx commit
`fea568087`): the same five recipes, restated for C#, and the same general rules. Its
playbook is meant to evolve from Jellyfin's own evidence.

---

## Scope

The scope is fixed in [`scope.json`](scope.json): the direct files of five folders and one
controller, about 32,000 lines.

| Folder | Role |
| --- | --- |
| `MediaBrowser.MediaEncoding/Probing` | Turning ffprobe output into media streams |
| `MediaBrowser.Controller/MediaEncoding` | Building ffmpeg command lines, transcoding jobs |
| `MediaBrowser.Model/Dlna` | The playback decision (direct play, stream copy, transcode) |
| `Emby.Server.Implementations/Library` | Library and media-source management, track selection |
| `Jellyfin.Api/Helpers` | Streaming and HLS helpers |
| `Jellyfin.Api/Controllers/DynamicHlsController.cs` | The HLS endpoints |

Nothing outside these files is part of the campaign. `docs/experiment/` belongs to a
different piece of work: do not read it, and do not prepare the code for any feature.

---

## Baseline

Measured with the CodeScene CLI (cs 1.0.46) on `d926869d7b`, in
[`scope-baseline.json`](scope-baseline.json).

| Band | Score | Files |
| --- | --- | --- |
| **Red** - severe debt | 1.0 - 3.9 | **5** |
| **Yellow** - problematic debt | 4.0 - 8.9 | **15** |
| Green | 9.0 - 9.9 | 13 |
| Optimal | 10.0 | 14 |
| **Total scored** | | **47** |

A further **28 files** have no methods to score (interfaces, enums, data classes); they are
out of scope. Weighted by lines of code, the scope scores **3.64**: `EncodingHelper.cs`
alone is 28% of that weight.

The **33 files below 10.00** are the backlog. Across them CodeScene finds **176 complex
methods**, **193 complex conditionals**, **97 bumpy roads**, and **25 deeply-nested
methods**. The campaign therefore leans mostly on three recipes: **extract method**,
**guard clauses**, and **named predicates**.

Some smells have no recipe in the playbook yet: *Excess Number of Function Arguments* (18
files), *Primitive Obsession* (11), *Constructor Over-Injection* (9), *Low Cohesion* (2),
*String Heavy Function Arguments* (2), *Number of Functions in a Single Module* (1). Task
files list them; agents report them and leave them.

### The extremes

| Method | File | Cyclomatic | Nesting |
| --- | --- | --- | --- |
| `GetVideoQualityParam` | `EncodingHelper.cs:2116` | **95** | - |
| `GetIntelQsvDx11VidFiltersPrefered` | `EncodingHelper.cs:4514` | 85 | - |
| `GetEncoderParam` | `EncodingHelper.cs:1726` | 80 | - |
| `GetMediaStream` | `ProbeResultNormalizer.cs:700` | 77 | 4 |

The recommended threshold is **cyclomatic complexity below 9** and **nesting depth below 4**.

---

## The central risk, and the safety net

Jellyfin serves media to many kinds of clients, and plugins build on its public API. A
refactor that changes which audio track is picked, or one character of an ffmpeg command
line, is a regression that no compiler warning will catch.

Unlike 3SX, this campaign has a behavioural safety net, built before any refactoring (see
[`test-harness.md`](test-harness.md)):

- The full test suite: about 5,900 tests, including acceptance tests that record the
  current behaviour at stable boundaries (the public API of the plugin packages, and HTTP)
  on Windows and on Linux.
- Mutation testing shows how much that net catches: **55.5%** of mutants in the scope are
  killed (Stryker) and **55.6%** under the Mull profile. Every file is at or above 30%,
  except three that the tests cannot reasonably reach.

The tests are therefore a backstop, not a proof. The recipes stay strictly behaviour-
preserving, and every check is automated. In the three files the tests barely cover, the
recipes are the only protection; their task files say so, and so must their reports.

Two further constraints come from the safety net itself:

- **The tests must not change.** Nothing under `tests/` is modified by the campaign.
- **The tests pin some structure.** The existing unit tests call nine internal members
  directly, and the plugin packages' public API is frozen. A smell that can only be removed
  by changing one of those stays, and the report says why. The known case is *Constructor
  Over-Injection* on `EncodingHelper`, whose six-argument constructor is plugin API.

---

## Phases

**Phase 0 - Stand up the safety net** *(done)*

The acceptance tests, the mutation baseline and the per-file floor are in place, recorded in
[`test-harness.md`](test-harness.md).

**Phase 1 - Pilot and calibration** *(next)*

Run the calibration probe in [`AGENT-SETUP.md`](AGENT-SETUP.md) on every harness, then work
one small file end to end. This phase tells you whether the task format works for the
agents you plan to use, on files where being wrong is cheap.

**Phase 2 - The backlog** *(after Phase 1)*

All 33 files, worst first: R01 `EncodingHelper.cs`, R02 `StreamBuilder.cs`, R03
`LibraryManager.cs`. After each file lands, re-run the mutation measurement for its part.

---

## Rules for every agent

1. Apply **only** the recipes in [`PLAYBOOK.md`](PLAYBOOK.md). Nothing else.
2. **One recipe, one method, one commit.**
3. Re-measure after every commit. Score did not improve, revert the commit.
4. Never change literals, arithmetic, comparisons, types, or the order of side effects.
5. Found a bug? **Report it, do not fix it.**
6. Never modify `tests/`, and never change a non-private member's name, signature or
   accessibility.
7. Stop at 10.00, or when every remaining smell has no recipe.
8. Blocked, confused, or the baseline score does not match? **Stop and report.**
   An unfinished task is a fine outcome.

---

## Definition of done, per file

- `codehealth_check.py` reports 10.00, or every remaining smell is one without a recipe,
  listed in the report
- `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- `dotnet test Jellyfin.sln --no-build` passes in full
- `refactor_guard.py --base <start commit>` reports no removed literals (`--together` with
  any files split off it)
- One method per commit, message format `refactor(<File>): simplify <Method>`
- Nothing under `tests/` changed

---

## Tracking progress

Re-measure the scope at any time:

```bash
python tools/codehealth_sweep.py -o docs/refactoring/scope-now.json <scope files>
python tools/scope_health.py --sweep docs/refactoring/scope-now.json
```

`scope_health.py` prints the LoC-weighted Code Health of the scope, the number the campaign
reports. After a file lands, re-run the mutation measurement for its part
(`tools/mutation/run.sh <part>`) and report both profiles with `tools/mutation/profiles.py`.

---

## Files in this directory

| Path | Purpose |
| --- | --- |
| [`README.md`](README.md) | This charter |
| [`PLAYBOOK.md`](PLAYBOOK.md) | The closed catalogue of allowed transformations |
| [`BACKLOG.md`](BACKLOG.md) | All 33 tasks, ranked, with baselines |
| [`AGENT-SETUP.md`](AGENT-SETUP.md) | Harness setup, work routing, calibration protocol |
| [`test-harness.md`](test-harness.md) | The safety net: suites, mutation scores, what the tests freeze |
| [`scope.json`](scope.json) | The scope |
| [`scope-baseline.json`](scope-baseline.json) | The committed baseline sweep of the scope |
| `tasks/R01..R33` | One self-contained task file per file below 10.00 |

Repo-root files that agents read: [`../../AGENTS.md`](../../AGENTS.md) (all harnesses) and
[`../../CLAUDE.md`](../../CLAUDE.md) (Claude Code). The tools are in [`../../tools/`](../../tools/):
`refactor_guard.py`, `codehealth_check.py`, `codehealth_sweep.py`, `scope_health.py`, and
`make_refactoring_tasks.py`, which regenerates the backlog and the task files.

## The mechanical guard

[`../../tools/refactor_guard.py`](../../tools/refactor_guard.py) compares every literal in a
file before and after a change; a removed or altered constant fails. It catches the single
most dangerous class of mistake - a changed magic number or string - with essentially no
false positives on legal refactors.

It does **not** check control flow. An inverted condition passes it. Together with the tests
it reduces risk; neither is a proof.
