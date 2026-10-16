#!/usr/bin/env python3
"""Measure one file's Code Health now and at a git ref, and say whether it went up.

The per-commit metric check of the refactoring campaign (docs/refactoring/). It runs
the CodeScene CLI on the working-tree file and on the committed version (by default
HEAD, i.e. before your uncommitted change), prints both scores and the smells that
remain, and ends with one verdict:

    UP     the score improved: keep the change
    FLAT   the score did not move: revert it
    DOWN   the score fell: revert it

The CLI is found the same way as in codehealth_sweep.py (CS_CLI, or the one bundled
with the CodeScene MCP server); CS_ACCESS_TOKEN must be set.

Usage:
    python tools/codehealth_check.py path/File.cs
    python tools/codehealth_check.py --base master path/File.cs

Exit codes: 0 UP, 2 FLAT or DOWN, 1 the file could not be measured.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from codehealth_sweep import find_cli  # noqa: E402


def review(exe: str, path: Path, cwd: Path) -> dict | None:
    env = dict(os.environ, CS_DISABLE_VERSION_CHECK="1")
    proc = subprocess.run([exe, "review", "--output-format=json", str(path)], cwd=cwd, env=env,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError:
        return None


def review_at(exe: str, rel: str, ref: str) -> dict | None:
    """Review the file as committed at ref. The CLI only runs inside a git repository."""
    content = subprocess.run(["git", "show", ref + ":" + rel], cwd=REPO, capture_output=True).stdout
    if not content:
        return None
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(["git", "init", "-q"], cwd=tmp, check=True)
        copy = Path(tmp) / Path(rel).name
        copy.write_bytes(content)
        return review(exe, copy, Path(tmp))


def show(score) -> str:
    return "none (no functions)" if score is None else f"{score:.2f}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("file", help="path relative to the repo root")
    ap.add_argument("--base", default="HEAD", help="git ref to compare against (default HEAD)")
    args = ap.parse_args()

    rel = args.file.replace("\\", "/")
    exe = find_cli()
    before = review_at(exe, rel, args.base)
    after = review(exe, REPO / rel, REPO)
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
        print(f"VERDICT: UP ({old:.2f} -> {new:.2f}) - keep it")
        return 0
    print(f"VERDICT: {'FLAT' if new == old else 'DOWN'} ({old:.2f} -> {new:.2f}) - revert with: git checkout -- {rel}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
