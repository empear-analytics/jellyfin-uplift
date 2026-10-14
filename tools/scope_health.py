#!/usr/bin/env python3
"""LoC-weighted Code Health of the experiment scope.

    python tools/scope_health.py                      # against the committed baseline
    python tools/scope_health.py --sweep other.json   # against another sweep

Reads the scope from docs/refactoring/scope.json (folders: direct files only;
files: listed individually) and the scores from a codehealth_sweep.py output.
Each scored file is weighted by its lines of code, counted without blank lines
and comments. Files with no functions have no score and carry no weight.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DOCS = REPO / "docs" / "refactoring"


def scope_files(scope: dict) -> list[str]:
    tracked = subprocess.run(["git", "ls-files", "*.cs"], cwd=REPO, capture_output=True,
                             text=True).stdout.split()
    folders = set(scope["folders"])
    files = [p for p in tracked if os.path.dirname(p) in folders]
    files += [p for p in scope["files"] if p not in files]
    missing = [p for p in files if not (REPO / p).is_file()]
    if missing:
        sys.exit("scope lists files that do not exist: " + ", ".join(missing))
    return sorted(files)


def code_lines(path: Path) -> int:
    """Lines that are neither blank nor comment-only."""
    count, in_block = 0, False
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        s = line.strip()
        if in_block:
            if "*/" not in s:
                continue
            in_block, s = False, s.split("*/", 1)[1].strip()
        if s.startswith("/*"):
            in_block = "*/" not in s
            continue
        if s and not s.startswith("//"):
            count += 1
    return count


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scope", default=str(DOCS / "scope.json"))
    ap.add_argument("--sweep", default=str(DOCS / "codehealth-baseline.json"))
    ap.add_argument("--files", action="store_true", help="list every file with its weight and score")
    args = ap.parse_args()

    scope = json.loads(Path(args.scope).read_text(encoding="utf-8"))
    sweep = json.loads(Path(args.sweep).read_text(encoding="utf-8"))
    scores = {f["path"]: f["raw_score"] for f in sweep["files"]}

    rows = [(p, code_lines(REPO / p), scores.get(p)) for p in scope_files(scope)]
    scored = [r for r in rows if r[2] is not None]
    weight = sum(r[1] for r in scored)
    weighted = sum(r[1] * r[2] for r in scored) / weight
    bands = {"red": 0, "yellow": 0, "green": 0, "optimal": 0}
    for _, _, s in scored:
        bands["optimal" if s >= 10 else "green" if s >= 9 else "yellow" if s >= 4 else "red"] += 1

    print("scope %s, sweep at %s" % (scope["name"], sweep.get("commit", "?")[:10]))
    print("files %d (%d scored, %d with no functions), %d lines of code in scored files"
          % (len(rows), len(scored), len(rows) - len(scored), weight))
    print("red %(red)d  yellow %(yellow)d  green %(green)d  optimal %(optimal)d" % bands)
    print("LoC-weighted Code Health: %.2f" % weighted)
    if args.files:
        for p, loc, s in sorted(scored, key=lambda r: r[2]):
            print("  %5.2f %6d  %s" % (s, loc, p))
    return 0


if __name__ == "__main__":
    sys.exit(main())
