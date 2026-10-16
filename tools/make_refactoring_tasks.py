#!/usr/bin/env python3
"""Generate the per-file task files and the backlog of the refactoring campaign.

Reads the scope (docs/refactoring/scope.json) and a Code Health sweep of it
(docs/refactoring/scope-baseline.json, written by codehealth_sweep.py) and writes
docs/refactoring/tasks/Rnn-<File>.md for every scope file below 10.00, ranked worst
first, plus docs/refactoring/BACKLOG.md. Smells are mapped to the recipes of
PLAYBOOK.md by the recipes' "use when"; a smell no recipe covers is listed as such,
to be reported rather than fixed.

Usage:
    python tools/make_refactoring_tasks.py
"""

from __future__ import annotations

import json
import re
import subprocess
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DOCS = REPO / "docs" / "refactoring"

# The recipes whose "use when" names the smell (PLAYBOOK.md). Anything else has no recipe.
RECIPES = {
    "Bumpy Road Ahead": "E",
    "Deep, Nested Complexity": "G",
    "Complex Conditional": "P",
    "Complex Method": "G, E, P",
    "Brain Method": "G, E, P",
    "Large Method": "E",
    "Code Duplication": "D",
    "Lines of Code in a Single File": "S (last)",
    "Overall Code Complexity": "follows the functions",
}

PARTS = {
    "MediaBrowser.MediaEncoding/Probing": ("probing", ["Jellyfin.MediaEncoding.Tests"]),
    "MediaBrowser.Controller/MediaEncoding": ("encoding", ["Jellyfin.Controller.Tests", "Jellyfin.Server.Integration.Tests"]),
    "MediaBrowser.Model/Dlna": ("dlna", ["Jellyfin.Model.Tests", "Jellyfin.Server.Integration.Tests"]),
    "Emby.Server.Implementations/Library": ("library", ["Jellyfin.Server.Implementations.Tests", "Jellyfin.Server.Integration.Tests"]),
    "Jellyfin.Api/Helpers": ("api", ["Jellyfin.Api.Tests", "Jellyfin.Server.Integration.Tests"]),
    "Jellyfin.Api/Controllers": ("api", ["Jellyfin.Api.Tests", "Jellyfin.Server.Integration.Tests"]),
}

PLUGIN_API = ("MediaBrowser.Controller/", "MediaBrowser.Model/")

# Internal members that the existing tests call directly; see test-harness.md, "What the tests freeze".
FROZEN_INTERNALS = {
    "MediaBrowser.MediaEncoding/Probing/ProbeResultNormalizer.cs": ["GetEstimatedAudioBitrate", "GetFrameRate", "IsNearSquarePixelSar"],
    "MediaBrowser.Model/Dlna/StreamBuilder.cs": ["ContainerReasons"],
    "Jellyfin.Api/Controllers/DynamicHlsController.cs": ["GetDolbyVisionHevcCodecTag", "GetSegmentLengthsInternal", "WaitForActiveTranscodingRequests"],
    "Jellyfin.Api/Helpers/MediaInfoHelper.cs": ["GetPublishedLiveStreamPath"],
    "Jellyfin.Api/Helpers/RequestHelpers.cs": ["GetUserId"],
}

NUMBER = re.compile(r"(\d+)")

# Files whose tests kill under this share of mutants are barely protected; their task files
# say so (test-harness.md documents why these stay untested).
PROTECTION_FLOOR = 0.30


def mutation_scores() -> dict[str, dict[str, float]]:
    """Per file and profile, the share of mutants killed (mutation-after.json)."""
    data = json.loads((DOCS / "mutation-after.json").read_text(encoding="utf-8"))
    scores: dict[str, dict[str, float]] = defaultdict(dict)
    for part in data.values():
        for profile, result in part["profiles"].items():
            for rel, counts in result["files"].items():
                total = sum(counts.values())
                if total:
                    scores[rel.replace("\\", "/")][profile] = (counts.get("Killed", 0) + counts.get("Timeout", 0)) / total
    return scores


def number(details: str | None) -> int | None:
    m = NUMBER.search(details or "")
    return int(m.group(1)) if m else None


def code_lines(path: Path) -> int:
    lines = 0
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        s = line.strip()
        if s and not s.startswith("//") and s not in "{}":
            lines += 1
    return lines


def churn(rel: str) -> int:
    out = subprocess.run(["git", "log", "--oneline", "--follow", "--", rel], cwd=REPO, capture_output=True, text=True).stdout
    return len(out.splitlines())


def functions(smells: list[dict]) -> list[dict]:
    """One row per flagged method, with the numbers each smell reports."""
    rows: dict[str, dict] = defaultdict(lambda: {"smells": set(), "line": None})
    for smell in smells:
        for f in smell["functions"]:
            name = f["title"].split(":")[0]
            row = rows[name]
            row["name"] = name
            row["smells"].add(smell["category"])
            line = f.get("start-line")
            if line and (row["line"] is None or (smell["category"] != "Complex Conditional" and line < row["line"])):
                row["line"] = line
            value = number(f.get("details"))
            key = {
                "Complex Method": "cc",
                "Deep, Nested Complexity": "nesting",
                "Bumpy Road Ahead": "bumps",
                "Complex Conditional": "conditionals",
                "Excess Number of Function Arguments": "arguments",
            }.get(smell["category"])
            if key and value is not None:
                row[key] = row.get(key, 0) + value if key == "conditionals" else value
    covered = set(RECIPES)
    ordered = sorted(rows.values(), key=lambda r: (-len(r["smells"] & covered), -(r.get("cc") or 0), -(r.get("nesting") or 0), r["name"]))
    return ordered


def recipes_for(row: dict) -> list[str]:
    steps = []
    if "Deep, Nested Complexity" in row["smells"]:
        steps.append(f"**Recipe G (guard clauses)** - nesting is {row.get('nesting')}, target is under 4.")
    if "Bumpy Road Ahead" in row["smells"]:
        steps.append(f"**Recipe E (extract function)** - {row.get('bumps')} bumps; each bump is a missing method.")
    if "Complex Conditional" in row["smells"]:
        steps.append(f"**Recipe P (named predicate)** - {row.get('conditionals')} complex conditional expression(s).")
    if row["smells"] & {"Complex Method", "Brain Method"} and not steps:
        steps.append(f"**Recipes G, E, P** - cyclomatic complexity {row.get('cc')}; reduce it with the recipes above as they apply.")
    if "Code Duplication" in row["smells"]:
        steps.append("**Recipe D (deduplicate)** - only if the copies are identical or differ by a single value.")
    for smell in sorted(row["smells"] - set(RECIPES)):
        detail = f" ({row['arguments']} arguments)" if smell == "Excess Number of Function Arguments" and row.get("arguments") else ""
        steps.append(f"*{smell}*{detail} - **no recipe in the playbook.** Leave it and report it.")
    return steps


def suitable(recipes: set[str]) -> str:
    if recipes & {"E", "D", "S"}:
        return "strongest available agent"
    if "G" in recipes:
        return "mid-capability agent or better"
    return "any agent"


def show(value) -> str:
    return "-" if value in (None, 0) else str(value)


def task_text(task_id: str, entry: dict) -> str:
    rel, score = entry["path"], entry["score"]
    stem = Path(rel).stem
    part, tests = PARTS[str(Path(rel).parent).replace("\\", "/")]
    rows = entry["functions"]
    recipes = sorted({r for s in entry["smells"] for r in RECIPES.get(s["category"], "").replace(" (last)", "").split(", ") if len(r) == 1})
    uncovered = sorted({s["category"] for s in entry["smells"]} - set(RECIPES))

    out = [f"# {task_id} - refactor `{rel}`", ""]
    out += ["| Field | Value |", "| --- | --- |"]
    out += [f"| Baseline Code Health | **{score:.2f}** / 10 ({entry['band'].capitalize()}) |"]
    out += ["| Exit target | **10.00** |"]
    out += [f"| File size | {entry['lines']} lines ({entry['code_lines']} lines of code) |"]
    out += [f"| Git churn | {entry['churn']} commits |"]
    out += [f"| Part | {part} (`{Path(rel).parent.as_posix()}`) |"]
    out += [f"| Recipes needed | {', '.join(recipes) if recipes else 'none'} |"]
    out += [f"| Smells without a recipe | {', '.join(uncovered) if uncovered else 'none'} |"]
    out += [f"| Suitable for | {suitable(set(recipes))} |"]
    mutation = entry.get("mutation", {})
    shown = " / ".join(f"{100 * mutation[p]:.0f}%" if p in mutation else "-" for p in ("stryker", "mull"))
    out += [f"| Mutation score (Stryker / Mull) | {shown} |", ""]
    if mutation and min(mutation.values()) < PROTECTION_FLOOR:
        out += ["> [!WARNING]"]
        out += ["> **The tests barely protect this file** (mutation score below 30%, see"]
        out += ["> [`../test-harness.md`](../test-harness.md)). A behaviour change here would most likely pass"]
        out += ["> every check, so the recipes are the only protection: apply them exactly. Work the task"]
        out += ["> like any other, and say in the report that the changes are effectively unverified.", ""]

    out += ["## 1. Read these first", ""]
    out += ["- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply."]
    out += ["- [`../README.md`](../README.md) - campaign rules and the definition of done.", ""]

    out += ["## 2. Record the baseline before touching anything", ""]
    out += ["```bash", f"python tools/codehealth_check.py {rel}", "```", ""]
    out += [f"The score must read {score:.2f}. If it does not, this file changed after the task was"]
    out += ["written - stop and report that instead of proceeding.", ""]

    out += ["## 3. What CodeScene flags here", "", "| Smell | Severity (1-3) | Recipe |", "| --- | --- | --- |"]
    for s in sorted(entry["smells"], key=lambda s: -(s.get("indication") or 0)):
        out += [f"| {s['category']} | {s.get('indication')} | {RECIPES.get(s['category'], 'none - report it')} |"]
    out += [""]
    if any(s["category"] == "Lines of Code in a Single File" for s in entry["smells"]):
        out += ["This file also trips **Lines of Code in a Single File**. Splitting it is the *last* step."]
        out += ["Reduce method-level complexity first; the file may drop below the limit without a split.", ""]

    out += ["## 4. What must not change here", ""]
    if rel.startswith(PLUGIN_API):
        out += ["- This file is in a **plugin-facing package**. Public and protected members are plugin API:"]
        out += ["  do not change their names, signatures or accessibility. New members you add are `private`."]
    else:
        out += ["- Public members may be called from the tests or other projects. The solution build, which"]
        out += ["  includes every test project, tells you if you broke one: never change a test to fix it."]
    if rel in FROZEN_INTERNALS:
        names = ", ".join(f"`{n}`" for n in FROZEN_INTERNALS[rel])
        out += [f"- The existing tests call these internal members directly: {names}. Keep each one, with"]
        out += ["  its name, signature and class. You may move its body into new private methods."]
    out += ["- Nothing in `tests/` may be modified.", ""]

    out += ["## 5. Target methods, highest leverage first", ""]
    out += ["| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |", "| --- | --- | --- | --- | --- | --- | --- |"]
    for i, r in enumerate(rows, 1):
        out += [f"| {i} | `{r['name']}` | {show(r['line'])} | {show(r.get('cc'))} | {show(r.get('nesting'))} | {show(r.get('bumps'))} | {show(r.get('conditionals'))} |"]
    out += ["", "Thresholds: cyclomatic complexity under 9, nesting depth under 4.", ""]

    out += ["## 6. Steps", ""]
    out += ["Work **one method at a time, in the order above**. After each method run the verification in"]
    out += ["section 7. Do not batch several methods into one change.", ""]
    for i, r in enumerate(rows, 1):
        out += [f"### Step {i}: `{r['name']}`" + (f" (line {r['line']})" if r["line"] else ""), ""]
        out += [f"- {step}" for step in recipes_for(r)]
        out += ["", f"Commit message: `refactor({stem}): simplify {r['name']}`", ""]
    file_level = [s["category"] for s in entry["smells"] if not s["functions"]]
    if file_level:
        out += ["### File-level smells", ""]
        for category in file_level:
            out += [f"- *{category}*: {RECIPES.get(category, 'no recipe in the playbook. Report it.')}"]
        out += [""]
    out += ["### Final step: re-measure, then stop", "", "Once the score reaches 10.00, **stop**.", ""]

    out += ["## 7. Verification - run all four after EVERY step, before committing it", ""]
    out += ["The tools compare your working tree with `HEAD`, so run them before `git commit`. To"]
    out += ["re-check a commit you already made, add `--base HEAD~1`.", ""]
    out += ["**1 - it still builds, warnings included.**", "", "```bash", "dotnet build Jellyfin.sln", "```", ""]
    out += ["Zero errors, and no new warning in the file you changed.", ""]
    out += ["**2 - the tests that cover this part still pass.**", "", "```bash"]
    out += [f"dotnet test tests/{t} --no-build" for t in tests]
    out += ["```", ""]
    out += ["**3 - no constant was removed or altered.**", "", "```bash", f"python tools/refactor_guard.py {rel}", "```", ""]
    out += ["A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor."]
    out += ["Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add"]
    out += ["`--dedup`; for Recipe S add `--together` and list the original file and the new ones.", ""]
    out += ["**4 - the metric improved.**", "", "```bash", f"python tools/codehealth_check.py {rel}", "```", ""]
    out += ["- `UP`, build passed, tests passed, guard passed: commit and continue."]
    out += [f"- `FLAT` or `DOWN`: revert that step with `git checkout -- {rel}` and move to the next method."]
    out += ["- Tests or guard **failed**: revert that step, no exceptions.", ""]

    out += ["## 8. Definition of done", ""]
    out += ["- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report"]
    out += ["- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)"]
    out += ["- [ ] `dotnet test Jellyfin.sln --no-build` passes in full"]
    out += [f"- [ ] `python tools/refactor_guard.py --base <start commit> --together {rel} <any files split off it>` reports no removed literals"]
    out += ["- [ ] Each commit covers one method and uses the message format above"]
    out += ["- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty", ""]

    out += ["## 9. Report back", "", "Reply with exactly this, filled in:", "", "```"]
    out += [f"Task: {task_id} ({rel})", f"Baseline score: {score:.2f}", "Final score:    <x.xx>"]
    out += [f"Steps completed: <n> of {len(rows)}", "Steps reverted:  <n>   (list which, and why)"]
    out += ["Smells left without a recipe: <list, or NONE>", "Build: PASS / FAIL", "Tests: PASS / FAIL"]
    if mutation and min(mutation.values()) < PROTECTION_FLOOR:
        out += ["Protection: LOW - the tests barely cover this file; the changes are effectively unverified"]
    out += ["Commits: <sha list>", "Unsure about: <free text, or NONE>", "```", ""]
    return "\n".join(out)


def existing_progress() -> dict[str, tuple[str, str]]:
    """The Current and Status columns of the backlog's progress table, kept across regenerations."""
    backlog = DOCS / "BACKLOG.md"
    if not backlog.is_file():
        return {}
    rows = {}
    in_progress = False
    for line in backlog.read_text(encoding="utf-8").splitlines():
        if line.startswith("## Progress"):
            in_progress = True
        elif in_progress and re.match(r"\| R\d+ \|", line):
            cells = [c.strip() for c in line.strip("|").split("|")]
            rows[cells[0]] = (cells[2], cells[3])
    return rows


def main() -> None:
    sweep = json.loads((DOCS / "scope-baseline.json").read_text(encoding="utf-8"))
    mutation = mutation_scores()
    entries = []
    for f in sweep["files"]:
        if f.get("score") is None or f["score"] >= 10:
            continue
        path = REPO / f["path"]
        entry = dict(f)
        entry["lines"] = len(path.read_text(encoding="utf-8-sig", errors="replace").splitlines())
        entry["code_lines"] = code_lines(path)
        entry["churn"] = churn(f["path"])
        entry["functions"] = functions(f["smells"])
        entry["mutation"] = mutation.get(f["path"], {})
        entries.append(entry)
    entries.sort(key=lambda e: (e["score"], -e["code_lines"]))

    tasks = DOCS / "tasks"
    tasks.mkdir(exist_ok=True)
    for old in tasks.glob("R*.md"):
        old.unlink()
    rows = []
    for i, entry in enumerate(entries, 1):
        task_id = f"R{i:02d}"
        name = f"{task_id}-{Path(entry['path']).stem}.md"
        (tasks / name).write_text(task_text(task_id, entry), encoding="utf-8", newline="\n")
        worst = max(entry["functions"], key=lambda r: r.get("cc") or 0, default=None)
        worst_text = f"`{worst['name']}` (cc {worst['cc']})" if worst and worst.get("cc") else "-"
        rows.append((task_id, name, entry, worst_text))

    out = ["# Backlog", ""]
    out += ["Every scope file below Code Health 10.00, ranked worst first. Generated by"]
    out += ["`tools/make_refactoring_tasks.py` from `scope-baseline.json` (sweep at "
            + sweep.get("commit", "?")[:10] + ").", ""]
    out += ["See [`README.md`](README.md) for the campaign rules and [`PLAYBOOK.md`](PLAYBOOK.md) for the"]
    out += ["allowed transformations.", ""]
    out += ["| Task | File | Score | LoC | Churn | Mutation (S / M) | Recipes | Smells without a recipe | Worst method |"]
    out += ["| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for task_id, name, e, worst in rows:
        recipes = sorted({r for s in e["smells"] for r in RECIPES.get(s["category"], "").replace(" (last)", "").split(", ") if len(r) == 1})
        uncovered = sorted({s["category"] for s in e["smells"]} - set(RECIPES))
        m = e["mutation"]
        low = " (low)" if m and min(m.values()) < PROTECTION_FLOOR else ""
        mutation_text = " / ".join(f"{100 * m[p]:.0f}" if p in m else "-" for p in ("stryker", "mull")) + low
        out += [f"| [{task_id}](tasks/{name}) | `{e['path']}` | **{e['score']:.2f}** | {e['code_lines']} | {e['churn']} | {mutation_text} | "
                f"{', '.join(recipes) or '-'} | {', '.join(uncovered) or '-'} | {worst} |"]
    total_loc = sum(e["code_lines"] for _, _, e, _ in rows)
    out += ["", "## Aggregate", ""]
    out += [f"- **{len(rows)} files** below 10.00, {total_loc:,} lines of code"]
    out += [f"- {sum(len(e['functions']) for _, _, e, _ in rows)} flagged methods"]
    out += ["- The other 14 scored scope files are already at 10.00; 28 scope files have no methods to score", ""]
    out += ["## Progress", "", "Update as tasks land.", "", "| Task | Baseline | Current | Status |", "| --- | --- | --- | --- |"]
    progress = existing_progress()
    for task_id, _, e, _ in rows:
        current, status = progress.get(task_id, ("-", "not started"))
        out += [f"| {task_id} | {e['score']:.2f} | {current} | {status} |"]
    (DOCS / "BACKLOG.md").write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {len(rows)} task files and BACKLOG.md")


if __name__ == "__main__":
    main()
