#!/usr/bin/env bash
# Mutation testing of the experiment scope (docs/refactoring/scope.json) with Stryker.NET.
#
#   tools/mutation/run.sh probing            # one part: probing, encoding, dlna, library, api
#   tools/mutation/run.sh all                # every part, one after the other
#
# One config per source project, each limited to the scope's files there and to
# the test projects that cover them (taken from the coverlet run). The
# integration tests are not listed, but Jellyfin.Server.Implementations.Tests
# references Jellyfin.Server.Integration.Tests, so they run in every part that
# uses it (encoding, library, api). That is what reaches MediaStreamSelector.
#
# Reports go outside the repo, to ../mutation/<part>/<timestamp>/.
# Each part runs with its own TMP/TEMP under ../mutation/tmp/, deleted when the
# part ends. Jellyfin's tests leave directories behind in the temp folder on
# every run (plugin-manager*, ...), and a mutation run repeats them thousands
# of times: on 2026-10-14 that put 3.7 GB of leftovers in %TEMP% and filled C:.
# Stryker is a local tool: run `dotnet tool restore` once. Every config sets
# test-runner mtp: xUnit v3 test projects run as their own executables, and under
# the default VSTest runner mutants are never switched on, so every mutant
# "survives" and the score is 0% with no error. target-framework is set because
# the test csproj files inherit it from tests/Directory.Build.props, which
# Stryker cannot see, and it then falls back to an MSBuild without .NET 10.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out_root="$(cd "$here/../../.." && pwd)/mutation"
parts=(probing encoding dlna library api)

run_part() {
    local part="$1" stamp tmp rc=0
    stamp="$(date +%Y%m%d-%H%M%S)"
    tmp="$out_root/tmp/$part-$stamp"
    mkdir -p "$tmp"
    echo "== $part ($(date +%T))"
    (cd "$here" && TMP="$tmp" TEMP="$tmp" TMPDIR="$tmp" \
        dotnet stryker -f "$part.json" -O "$out_root/$part/$stamp" --skip-version-check) || rc=$?
    rm -rf "$tmp"
    return "$rc"
}

case "${1:-}" in
    all) for p in "${parts[@]}"; do run_part "$p"; done ;;
    probing|encoding|dlna|library|api) run_part "$1" ;;
    *) echo "usage: $0 {${parts[*]// /|}|all}" >&2; exit 2 ;;
esac
