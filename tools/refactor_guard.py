#!/usr/bin/env python3
"""Mechanically check that a refactor did not change behaviour-carrying tokens.

The refactoring campaign (docs/refactoring/) allows only structural changes:
extracting methods, adding guard clauses, naming predicates. None of those
may alter a numeric literal, a string, or a character constant.

This script extracts every literal from a C# file before and after a change
and compares the multisets. If a constant appeared, vanished, or changed value,
the refactor is not behaviour-preserving and must be reverted.

It is deliberately dumb and has no opinion about style. It answers exactly one
question: did any constant change? Strings are compared as whole tokens, so an
interpolated string counts as one literal, holes included; a string whose
interpolation hole itself contains a quoted string may be split oddly, but it is
split the same way before and after a pure move.

Ported from the 3SX campaign's tools/refactor_guard.py (C) to C#.

Two recipes move or remove literals by design, and have their own mode:

    --together  Recipe S (split file): the original file and the files split off it are
                compared as one unit, so a literal that moved between them still counts.
    --dedup     Recipe D (deduplicate): a literal may occur fewer times than before, as long
                as it still occurs, and no value new to the file may appear.

Usage:
    python tools/refactor_guard.py path/File.cs            # working tree vs HEAD
    python tools/refactor_guard.py --base master path/File.cs
    python tools/refactor_guard.py --all                    # every changed .cs
    python tools/refactor_guard.py --together path/File.cs path/File.Theme.cs
    python tools/refactor_guard.py --dedup path/File.cs

Exit codes:
    0  no literal changed
    1  a literal changed, or the file could not be compared
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# Order matters: strings and chars are consumed before numbers so that digits
# inside a string are not mistaken for numeric literals, and raw and verbatim
# strings before regular ones.
STRING = r"""
      (?:\$*\"{3,}[\s\S]*?\"{3,})                    # raw string literal
    | (?:(?:\$@|@\$|@)"(?:[^"]|"")*")                 # verbatim (optionally interpolated)
    | (?:\$?"(?:[^"\\\n]|\\.)*")                      # regular (optionally interpolated)
"""
TOKEN_RE = re.compile(
    r"""
      (?P<string>""" + STRING + r""")
    | (?P<char>'(?:[^'\\\n]|\\.)+')
    | (?P<number>
          (?<![\w.])0[xX][0-9a-fA-F_]+(?:[uU][lL]?|[lL][uU]?)?
        | (?<![\w.])0[bB][01_]+(?:[uU][lL]?|[lL][uU]?)?
        | (?<![\w])(?:\d[\d_]*\.\d[\d_]*|\.\d[\d_]*|\d[\d_]*)(?:[eE][+-]?\d+)?(?:[fFdDmM]|[uU][lL]?|[lL][uU]?)?
      )
    """,
    re.VERBOSE,
)

COMMENT_RE = re.compile(
    r"""
      (?P<keep>""" + STRING + r"""|'(?:[^'\\\n]|\\.)+')
    | (?P<block>/\*.*?\*/)
    | (?P<line>//[^\n]*)
    """,
    re.VERBOSE | re.DOTALL,
)


def strip_comments(src: str) -> str:
    def repl(m: re.Match) -> str:
        if m.group("keep"):
            return m.group("keep")
        return " "
    return COMMENT_RE.sub(repl, src)


def normalise_number(tok: str) -> str:
    """Fold away suffix, separator and format differences that do not change the value."""
    body = tok.replace("_", "")
    lower = body.lower()
    try:
        if lower.startswith("0x"):
            return str(int(body.rstrip("uUlL"), 16))
        if lower.startswith("0b"):
            return str(int(body.rstrip("uUlL"), 2))
        body = body.rstrip("uUlLfFdDmM")
        if any(c in body for c in ".eE"):
            return repr(float(body))
        return str(int(body))
    except ValueError:
        return tok


def literals(src: str) -> Counter:
    out = Counter()
    for m in TOKEN_RE.finditer(strip_comments(src)):
        if m.group("string"):
            out["str " + m.group("string")] += 1
        elif m.group("char"):
            out["chr " + m.group("char")] += 1
        else:
            out["num " + normalise_number(m.group("number"))] += 1
    return out


def git_show(ref: str, rel: str) -> str | None:
    proc = subprocess.run(
        ["git", "show", ref + ":" + rel],
        cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    return proc.stdout if proc.returncode == 0 else None


def changed_files(base: str) -> list[str]:
    proc = subprocess.run(
        ["git", "diff", "--name-only", base, "--", "*.cs"],
        cwd=REPO, capture_output=True, text=True,
    )
    return [p.strip() for p in proc.stdout.splitlines() if p.strip()]


def read_pair(rel: str, base: str, together: bool) -> tuple[str, str] | None:
    """The file as committed at base and as it is now; None if it cannot be compared."""
    before = git_show(base, rel)
    path = REPO / rel
    after = path.read_text(encoding="utf-8-sig", errors="replace") if path.is_file() else None
    if before is None and not together:
        print("SKIP  " + rel + "  (not in " + base + "; new file)")
        return None
    if after is None and not together:
        print("FAIL  " + rel + "  (deleted from working tree)")
        return ("", "")
    return ((before or "").lstrip("﻿"), after or "")


def check(label: str, a: Counter, b: Counter, strict: bool = False, dedup: bool = False) -> bool:
    """Return True if the literals are clean.

    A *removed* literal is the dangerous signal: a constant was altered
    (removal plus addition of the new value) or code carrying it was deleted.
    That fails the check. With dedup, a literal may occur fewer times, as long
    as it still occurs somewhere, and then no value new to the file may appear.

    A purely *added* literal is normally benign - a new guard clause brings its
    own value, an extracted helper its own bounds check - so it is reported as a
    warning. Use --strict to fail on those too.
    """
    removed = a - b
    added = b - a
    novel = [lit for lit in added if lit not in a]

    if not removed and not added:
        print("OK    " + label + "  (" + str(sum(a.values())) + " literals unchanged)")
        return True

    gone = [lit for lit in removed if b[lit] == 0] if dedup else list(removed)
    if gone or (dedup and novel):
        print("FAIL  " + label + "  - a constant was removed or altered")
        for lit, n in sorted(removed.items()):
            note = "" if lit in gone else "  (still present: fewer copies)"
            print("        removed x" + str(n) + "  " + lit + note)
        for lit, n in sorted(added.items()):
            print("        added   x" + str(n) + "  " + lit + ("  <-- value is new to this file" if lit in novel else ""))
        return False

    if removed:
        print("DEDUP " + label + "  - fewer copies of " + str(len(removed)) + " literal(s), every value still present")
        for lit, n in sorted(removed.items()):
            print("        fewer   x" + str(n) + "  " + lit)
        if not added:
            return True

    label_kind = "WARN " if not strict else "FAIL "
    print(label_kind + " " + label + "  - literals added, none removed")
    for lit, n in sorted(added.items()):
        tag = "  <-- value is new to this file" if lit in novel else ""
        print("        added   x" + str(n) + "  " + lit + tag)
    if novel:
        print("        review the new values above; the rest are already used elsewhere here")
    return not strict


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="*", help="paths relative to the repo root")
    ap.add_argument("--base", default="HEAD", help="git ref to compare against (default HEAD)")
    ap.add_argument("--all", action="store_true", help="check every changed .cs")
    ap.add_argument("--strict", action="store_true",
                    help="also fail when literals are added (not just removed)")
    ap.add_argument("--together", action="store_true",
                    help="Recipe S: compare the given files as one unit (new files start empty)")
    ap.add_argument("--dedup", action="store_true",
                    help="Recipe D: allow fewer copies of a literal that is still present")
    args = ap.parse_args()

    targets = changed_files(args.base) if args.all else args.files
    if not targets:
        print("nothing to check")
        return 0
    targets = [rel.replace("\\", "/") for rel in targets]

    ok = True
    if args.together:
        a, b = Counter(), Counter()
        for rel in targets:
            before, after = read_pair(rel, args.base, together=True)
            a += literals(before)
            b += literals(after)
        ok = check(" + ".join(targets), a, b, args.strict, args.dedup)
    else:
        for rel in targets:
            pair = read_pair(rel, args.base, together=False)
            if pair is None:
                continue
            if pair == ("", "") and not (REPO / rel).is_file():
                ok = False
                continue
            if not check(rel, literals(pair[0]), literals(pair[1]), args.strict, args.dedup):
                ok = False

    print()
    if ok:
        print("PASS - no constant was removed or altered.")
        return 0
    print("BLOCKED - this is not a legal campaign refactor.")
    print("Revert with:  git checkout -- <file>")
    return 1


if __name__ == "__main__":
    sys.exit(main())
