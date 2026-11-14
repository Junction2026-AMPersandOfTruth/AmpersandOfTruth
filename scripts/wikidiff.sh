#!/usr/bin/env bash
# The C# wikitext diff engine (#29) from the shell / research scripts — one implementation, no Python port to drift.
#
#   scripts/wikidiff.sh batch < pairs.jsonl > diffs.jsonl   # {"id": …, "old": "<wikitext>"|null, "new": "<wikitext>"} per line
#   scripts/wikidiff.sh one --new b.txt [--old a.txt]        # pretty JSON for one pair (no --old = first revision)
#   scripts/wikidiff.sh prose < page.txt                     # prose view + references of one revision
#
# One result per input line, same order (blank lines skipped); a bad line gives {"id", "contract", "error"} and the batch
# goes on. Output contract: research/eval/CONTRACT.md §9. JSON → stdout, build output and timing → stderr.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet build "$ROOT/tools/AmpersandOfTruth.WikiDiff" -c Release -v quiet --nologo -clp:ErrorsOnly >&2
exec dotnet "$ROOT/tools/AmpersandOfTruth.WikiDiff/bin/Release/net10.0/AmpersandOfTruth.WikiDiff.dll" "$@"
