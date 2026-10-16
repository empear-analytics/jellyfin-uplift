#!/usr/bin/env python3
"""Mutation scores of Stryker.NET reports under named operator profiles.

    python tools/mutation/profiles.py                 # latest report of every part
    python tools/mutation/profiles.py probing dlna    # latest report of these parts
    python tools/mutation/profiles.py --files         # per file as well as per part
    python tools/mutation/profiles.py --json out.json

Stryker runs its "Standard" operator set. Every figure is reported under two
profiles, both read from the same report, so no extra run is needed:

    stryker   every mutant Stryker tested ("Standard" level)
    mull      the operators of the ImageMagick and 3SX harnesses: Mull's
              cxx_default (equality, relational and boundary, arithmetic and
              compound assignment, increment and decrement, unary minus) plus
              cxx_remove_void_call (deleting a call whose result is unused)

The void-call part is syntactic: a Stryker statement removal counts when the
removed statement is a call whose result is discarded (`Foo(x);`,
`obj.Bar();`, `await Baz(...).ConfigureAwait(false);`). C# can discard a
non-void result, such as the bool of Dictionary.TryAdd, so this slightly
over-counts against Mull, which checks that the callee returns void.

Platforms: some branches only run on one operating system, so a part can have
reports from several (../mutation/<part>/ from Windows, <part>-linux/, <part>-macos/).
The latest report of each is merged per mutant, matched by file, position,
operator and replacement: a mutant killed on any platform counts as killed, one
no platform covers counts as not covered, anything else as survived.

Scores:
    score     killed / (killed + survived + no coverage)   Stryker's own figure
    covered   killed / (killed + survived)                 on mutants some test runs
Timeouts count as killed; compile errors and ignored mutants are left out.
"""

from __future__ import annotations

import argparse
import collections
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
OUT_ROOT = REPO.parent / "mutation"
PARTS = ("probing", "encoding", "dlna", "library", "api")

MULL_DEFAULT = {
    "Equality mutation",
    "Arithmetic mutation",
    "PostIncrementExpression to PostDecrementExpression mutation",
    "PostDecrementExpression to PostIncrementExpression mutation",
    "PreIncrementExpression to PreDecrementExpression mutation",
    "PreDecrementExpression to PreIncrementExpression mutation",
    "UnaryMinusExpression to UnaryPlusExpression mutation",
    "AddAssignmentExpression to SubtractAssignmentExpression mutation",
    "SubtractAssignmentExpression to AddAssignmentExpression mutation",
    "MultiplyAssignmentExpression to DivideAssignmentExpression mutation",
    "DivideAssignmentExpression to MultiplyAssignmentExpression mutation",
    "ModuloAssignmentExpression to MultiplyAssignmentExpression mutation",
}
VOID_CALL = re.compile(
    r"^(await\s+)?[\w.?\[\]<>]+\s*\(.*\)(\s*\.ConfigureAwait\((true|false)\))?\s*;?$", re.S)
TESTED = ("Killed", "Timeout", "Survived", "NoCoverage")


def original_text(source_lines: list[str], loc: dict) -> str:
    s, e = loc["start"], loc["end"]
    if s["line"] == e["line"]:
        return source_lines[s["line"] - 1][s["column"] - 1:e["column"] - 1]
    parts = [source_lines[s["line"] - 1][s["column"] - 1:]]
    parts += source_lines[s["line"]:e["line"] - 1]
    parts.append(source_lines[e["line"] - 1][:e["column"] - 1])
    return "\n".join(parts)


def is_void_call(text: str) -> bool:
    text = text.strip()
    head = text.split("(", 1)[0]
    return bool(VOID_CALL.match(text)) and "=" not in head and not head.startswith(("return", "throw"))


def in_profile(profile: str, mutant: dict, source_lines: list[str]) -> bool:
    if profile == "stryker":
        return True
    if mutant["mutatorName"] in MULL_DEFAULT:
        return True
    return (mutant["mutatorName"] == "Statement mutation"
            and is_void_call(original_text(source_lines, mutant["location"])))


PLATFORMS = ("", "-linux", "-macos")
PROJECT_PATH = re.compile(r"(?:^|/)((?:MediaBrowser|Emby|Jellyfin)\.[^/]+/.*|src/.*)$")
RANK = {"NoCoverage": 1, "Survived": 2, "Timeout": 3, "Killed": 3}


def latest_report(part: str) -> Path | None:
    runs = sorted((OUT_ROOT / part).glob("*/reports/mutation-report.json"))
    return runs[-1] if runs else None


def repo_relative(path: str) -> str:
    path = path.replace("\\", "/")
    match = PROJECT_PATH.search(path)
    return match.group(1) if match else path


def tally(reports: list[Path]) -> dict:
    """{profile: {file: Counter(status)}}, merging the reports per mutant."""
    best = {}
    for report in reports:
        data = json.loads(report.read_text(encoding="utf-8"))
        for path, f in data["files"].items():
            rel = repo_relative(path)
            lines = f["source"].split("\n")
            for m in f["mutants"]:
                if m["status"] not in TESTED:
                    continue
                loc = m["location"]
                key = (rel, loc["start"]["line"], loc["start"]["column"], loc["end"]["line"],
                       loc["end"]["column"], m["mutatorName"], m.get("replacement"))
                if key not in best or RANK[m["status"]] > RANK[best[key][0]["status"]]:
                    best[key] = (m, lines)
    out = {p: collections.defaultdict(collections.Counter) for p in ("stryker", "mull")}
    for (rel, *_), (m, lines) in best.items():
        for profile in out:
            if in_profile(profile, m, lines):
                out[profile][rel][m["status"]] += 1
    return out


def scores(c: collections.Counter) -> tuple[int, float | None, float | None]:
    killed = c["Killed"] + c["Timeout"]
    n = killed + c["Survived"] + c["NoCoverage"]
    covered = killed + c["Survived"]
    return n, (100 * killed / n if n else None), (100 * killed / covered if covered else None)


def fmt(v: float | None) -> str:
    return "  n/a" if v is None else "%5.1f" % v


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("parts", nargs="*", default=list(PARTS))
    ap.add_argument("--files", action="store_true", help="show every file")
    ap.add_argument("--json", help="also write the figures to this file")
    ap.add_argument("--root", help="read reports from this folder instead of ../mutation")
    args = ap.parse_args()
    if args.root:
        global OUT_ROOT
        OUT_ROOT = Path(args.root).resolve()

    result, grand = {}, {p: collections.Counter() for p in ("stryker", "mull")}
    print("%-10s %-8s %6s %7s %8s" % ("part", "profile", "tested", "score%", "covered%"))
    for part in args.parts:
        reports = [r for r in (latest_report(part + suffix) for suffix in PLATFORMS) if r]
        if not reports:
            print("%-10s no report under %s" % (part, OUT_ROOT / part))
            continue
        t = tally(reports)
        result[part] = {"reports": [str(r) for r in reports], "profiles": {}}
        for profile, files in t.items():
            total = sum(files.values(), collections.Counter())
            grand[profile] += total
            n, s, cov = scores(total)
            print("%-10s %-8s %6d %7s %8s" % (part, profile, n, fmt(s), fmt(cov)))
            result[part]["profiles"][profile] = {
                "total": dict(total),
                "files": {f: dict(c) for f, c in files.items()},
            }
            if args.files:
                for f, c in sorted(files.items()):
                    n, s, cov = scores(c)
                    print("    %6d %7s %8s  %s" % (n, fmt(s), fmt(cov), f))
    if len(result) > 1:
        for profile, c in grand.items():
            n, s, cov = scores(c)
            print("%-10s %-8s %6d %7s %8s" % ("ALL", profile, n, fmt(s), fmt(cov)))
    if args.json:
        Path(args.json).write_text(json.dumps(result, indent=1) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
