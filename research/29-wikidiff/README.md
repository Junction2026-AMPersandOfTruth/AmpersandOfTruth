# #29 · P1.S2 wikitext diff engine: shared fixtures

The engine is production code (`src/AmpersandOfTruth.Web/Features/WikiDiff/`), so experiments in any language call
the one implementation through `scripts/wikidiff.sh` instead of porting it. The output contract is
`research/eval/CONTRACT.md` §9.

| File | |
|---|---|
| `fixtures.jsonl` | 12 requests `{"id", "old", "new"}` built from the xUnit fixtures (`tests/…/Features/WikiDiff/Fixtures/`): small insertion, paragraph rewrite, moved section, reference swap, template-only change, infobox value, section blanking, category/interwiki only, markup only, a Finnish name swap, a first revision, an unclosed `<ref>`. |
| `expected.jsonl` | The engine's answer. An xUnit test fails when the engine's output drifts from it. |
| `check.py` | `python3 research/29-wikidiff/check.py` checks the C# CLI exactly. `-- <command>` checks any other implementation on `stats`, `added_prose` and `removed_prose`. |

## Using it from an experiment

```bash
scripts/wikidiff.sh batch < pairs.jsonl > diffs.jsonl      # one start-up for the whole batch
```

The wrapper rebuilds first, which takes 2–3 s. In a loop, build once and then call the DLL directly
(`dotnet tools/AmpersandOfTruth.WikiDiff/bin/Release/net10.0/AmpersandOfTruth.WikiDiff.dll batch`). Its start-up is
about 0.16 s, so send many pairs per call either way.

## Speed

Measured on synthetic fi-style articles sized from the corpus profile (`prof_rev` Q8: median 3.8 KB, p99 52 KB, max
199 KB), Release build, warm, on the 4-core cloud container. Engine only; JSON adds about 0.1 ms per typical diff.

| Case | Median | p95 |
|---|---:|---:|
| Small edit, 4 KB article (n = 1,000) | 0.09 ms | 0.17 ms |
| Small edit, 52 KB article (n = 100) | 0.5 ms | 2.7 ms |
| Small edit, 200 KB article (n = 20) | 2.6 ms | 2.9 ms |
| 200 KB article blanked (n = 20) | 24 ms | 69 ms |
| 52 KB article fully rewritten from the same vocabulary (n = 20) | 23 ms | 59 ms |
| Prose view of a 195 KB article (`wikidiff prose`) | 33 ms | |

Through the CLI, including JSON in and out: 1,000 typical pairs in 1.5 s, 100 pairs of 52 KB in 1.0 s.

## Unclosed `<ref>` (fixed 2026-11-14, found by S7 #34)

An edit that deleted or mistyped a `</ref>` used to be reported as one **added reference** holding everything up to the
next reference's closing tag (headings and whole paragraphs the edit never touched), and that text as **removed prose**.
S7's cluster `#3323` linked 17 such edits by 13 accounts on *Talvisota* through the swallowed section. Now a `<ref>` is
a reference only if its own `</ref>` comes before the next `<ref …>` and before the next section heading
(`RefTags`, CONTRACT.md §9). On the `unclosed-ref` fixture the diff went from 172 removed and 0 added prose characters
with 2 removed and 1 added reference to 0 removed and 40 added (the words the edit typed) with 1 removed reference.
The other 11 fixtures are byte-identical.

**Diffs computed before this are stale for such edits.** Before S7 or S8 are re-run, `research.s3_diff` has to be
emptied and rebuilt (`research/30-s3-extract/diffcache.py` only fills in missing rows), and S8's
`~/.cache/aot-research/s8/candidates.jsonl` regenerated. The detectors' own workarounds for the swallow
(`_split_unclosed_refs`, `swallowed_heading` in `research/35-s8-signals`) should then no longer trigger. The app's
cached readings are invalidated by `CorpusEditReader.Version`.

## Open (needs the corpus, so for local Claude)

- **#29 "Done when" part 2:** diff 1,000 real flagged edits (e.g. the S1 reverted edits) through `wikidiff batch`
  and confirm they fit the budget.
- **#29 "Done when" part 3:** spot-check segment classification on 30 real edits. Look in particular at infobox
  values (a multi-line template's values count as prose), tables, `{{lippu|…}}`-style nested templates (their
  values do not count) and the blanking thresholds.
