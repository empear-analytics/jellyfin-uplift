#!/usr/bin/env bash
# Mutation testing of the experiment scope (docs/refactoring/scope.json) with Stryker.NET.
#
#   tools/mutation/run.sh probing            # one part: probing, encoding, dlna, library, api
#   tools/mutation/run.sh all                # every part, one after the other
#
# One config per source project, each limited to the scope's files there and to
# the test projects that cover them (taken from the coverlet run). The
# integration tests are left out: they start the whole server and would make
# every mutant cost about a minute. MediaStreamSelector is covered only by them,
# so its mutants will show as "no coverage" here.
#
# Reports go outside the repo, to ../mutation/<part>/<timestamp>/.
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
    local part="$1" stamp
    stamp="$(date +%Y%m%d-%H%M%S)"
    echo "== $part ($(date +%T))"
    (cd "$here" && dotnet stryker -f "$part.json" -O "$out_root/$part/$stamp" --skip-version-check)
}

case "${1:-}" in
    all) for p in "${parts[@]}"; do run_part "$p"; done ;;
    probing|encoding|dlna|library|api) run_part "$1" ;;
    *) echo "usage: $0 {${parts[*]// /|}|all}" >&2; exit 2 ;;
esac
