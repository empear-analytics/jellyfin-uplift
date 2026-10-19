#!/usr/bin/env python3
"""Measure one file's Code Health now and at a git ref, and say whether it went up.

The per-commit metric check of the refactoring campaign (docs/refactoring/). It reviews
the working-tree file and the committed version (by default HEAD, i.e. before your
uncommitted change) through the CodeScene MCP server, so that every measurement is
recorded on the CodeScene side, prints both scores and the smells that remain, and ends
with one verdict:

    UP     the score improved, with no new smell and no method made worse: keep the change
    WORSE  the score improved, but a smell category is new to the file, a method that was
           already flagged got a higher number (complexity, nesting, bumps...), or a new
           method has Excess Number of Function Arguments: revert it
    FLAT   the score did not move: revert it
    DOWN   the score fell: revert it

The MCP server is started from the "codescene" entry of the repository's .mcp.json (see
cs_mcp_client.py). When it cannot be reached, or returns nothing for a file, the script
falls back to the CodeScene CLI, found the same way as in codehealth_sweep.py (CS_CLI, or
the one bundled with the CodeScene MCP server). CS_ACCESS_TOKEN must be set either way.

Usage:
    python tools/codehealth_check.py path/File.cs
    python tools/codehealth_check.py --base master path/File.cs
    python tools/codehealth_check.py --backend cli path/File.cs    # skip the MCP server
    python tools/codehealth_check.py --backend mcp path/File.cs    # never fall back

Exit codes: 0 UP, 2 WORSE, FLAT or DOWN, 1 the file could not be measured.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from codehealth_sweep import find_cli  # noqa: E402
from cs_mcp_client import McpClient, McpUnavailable, parse_review  # noqa: E402

BACKEND = "auto"  # auto: the MCP server, then the CLI; mcp: the MCP server only; cli: the CLI only
_mcp: McpClient | None = None
_mcp_down = False


def mcp_review(path: Path) -> dict | None:
    """Review one file through the CodeScene MCP server, or None when it cannot."""
    global _mcp, _mcp_down
    if BACKEND == "cli" or _mcp_down:
        return None
    try:
        if _mcp is None:
            _mcp = McpClient.from_repo(REPO)
        data = parse_review(_mcp.call("code_health_review", {"file_path": str(path)}))
    except McpUnavailable as exc:
        _mcp_down = True
        print(f"note: the CodeScene MCP server is unavailable ({exc})", file=sys.stderr)
        return None
    if data is None:
        print(f"note: the CodeScene MCP server gave no review for {path.name}", file=sys.stderr)
    return data


def review(exe: str | None, path: Path, cwd: Path) -> dict | None:
    data = mcp_review(path)
    if data is not None or BACKEND == "mcp":
        return data
    if exe is None:
        exe = find_cli()
    env = dict(os.environ, CS_DISABLE_VERSION_CHECK="1")
    proc = subprocess.run([exe, "review", "--output-format=json", str(path)], cwd=cwd, env=env,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError:
        return None


def review_at(exe: str | None, rel: str, ref: str) -> dict | None:
    """Review the file as committed at ref. The CLI only runs inside a git repository."""
    content = subprocess.run(["git", "show", ref + ":" + rel], cwd=REPO, capture_output=True).stdout
    if not content:
        return None
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(["git", "init", "-q"], cwd=tmp, check=True)
        copy = Path(tmp) / Path(rel).name
        copy.write_bytes(content)
        return review(exe, copy, Path(tmp))


NUMBER = re.compile(r"(\d+)")
EXCESS_ARGUMENTS = "Excess Number of Function Arguments"


def findings(review: dict) -> tuple[set[str], dict[tuple[str, str], int]]:
    """Smell categories in the file, and the largest number each method's findings report.

    A method can have several findings of one category (two complex conditionals, say).
    Compare the largest, so removing the smaller one does not count as a regression.
    """
    categories, numbers = set(), {}
    for item in review.get("review", []):
        categories.add(item.get("category"))
        for f in item.get("functions", []):
            m = NUMBER.search(f.get("details") or "")
            key = (item.get("category"), f.get("title", "").split(":")[0])
            numbers[key] = max(numbers.get(key, 0), int(m.group(1)) if m else 0)
    return categories, numbers


def regressions(before: dict, after: dict) -> list[str]:
    """What the CodeScene PR gate would reject even when the score rose."""
    old_categories, old_numbers = findings(before)
    new_categories, new_numbers = findings(after)
    found = [f"new smell: {c}" for c in sorted(new_categories - old_categories)]
    for key, value in sorted(new_numbers.items()):
        if key in old_numbers and value > old_numbers[key]:
            found.append(f"worse: {key[0]} in {key[1]} ({old_numbers[key]} -> {value})")
        elif key not in old_numbers and key[0] == EXCESS_ARGUMENTS:
            found.append(f"new method with {EXCESS_ARGUMENTS}: {key[1]} ({value})")
    return found


# A parameter declared with a verbatim identifier, `bool? @static,` (a type, then @name). Calls
# pass `@static` after `(` or `,`, so they do not match.
VERBATIM_PARAMETER = re.compile(r"[\w>\]?]\s+@[A-Za-z_]\w*\s*[,)=]")


def verbatim_parameters(text: str) -> int:
    """How many verbatim-identifier parameters the file declares.

    CodeScene's C# parser silently drops a method whose parameter list contains such a name
    (found in R2-10: a 53-argument extraction vanished from the review and scored UP), so an
    extraction that adds one would be scored without it.
    """
    return len(VERBATIM_PARAMETER.findall(text))


def show(score) -> str:
    return "none (no functions)" if score is None else f"{score:.2f}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("file", help="path relative to the repo root")
    ap.add_argument("--base", default="HEAD", help="git ref to compare against (default HEAD)")
    ap.add_argument("--backend", choices=("auto", "mcp", "cli"), default="auto",
                    help="auto (default): the MCP server, then the CLI; mcp: the server only; cli: the CLI only")
    args = ap.parse_args()
    global BACKEND
    BACKEND = args.backend

    rel = args.file.replace("\\", "/")
    exe = find_cli() if BACKEND == "cli" else None
    try:
        before = review_at(exe, rel, args.base)
        after = review(exe, REPO / rel, REPO)
    finally:
        if _mcp is not None:
            _mcp.close()
    if after is None:
        print("could not measure " + rel)
        return 1

    print(f"{rel}")
    print(f"  {args.base:>8}: {show(before.get('score') if before else None)}")
    print(f"  {'now':>8}: {show(after.get('score'))}")
    for item in after.get("review", []):
        functions = item.get("functions", [])
        print(f"  - {item.get('category')} (indication {item.get('indication')})")
        for f in functions:
            print(f"      {f.get('title')}: {f.get('details', '')} [lines {f.get('start-line')}-{f.get('end-line')}]")

    old = before.get("score") if before else None
    new = after.get("score")
    if old is None or new is None:
        print("VERDICT: cannot compare (one side has no score)")
        return 1
    if new > old:
        worse = regressions(before, after)
        old_text = subprocess.run(["git", "show", args.base + ":" + rel], cwd=REPO, capture_output=True,
                                  text=True, encoding="utf-8", errors="replace").stdout
        new_text = (REPO / rel).read_text(encoding="utf-8-sig", errors="replace")
        if verbatim_parameters(new_text) > verbatim_parameters(old_text):
            worse.append("a new parameter named with @ (such as `bool? @static`): CodeScene skips such "
                         "methods, so the score cannot be trusted")
        if worse:
            for line in worse:
                print("  ! " + line)
            print(f"VERDICT: WORSE ({old:.2f} -> {new:.2f}, but see above) - revert with: git checkout -- {rel}")
            return 2
        print(f"VERDICT: UP ({old:.2f} -> {new:.2f}) - keep it")
        return 0
    print(f"VERDICT: {'FLAT' if new == old else 'DOWN'} ({old:.2f} -> {new:.2f}) - revert with: git checkout -- {rel}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
