#!/usr/bin/env python3
"""Run P1.S8 text-group detectors ('number_changed', 'attribution_flip', 'loaded_term') on the shared S8 candidates.

    python3 research/35-s8-signals/run_text.py

Reads $AOT_CACHE_DIR/s8/candidates.jsonl (default ~/.cache/aot-research/s8/candidates.jsonl) and writes:
  - research/35-s8-signals/results/text.jsonl  ({rev_id, group, signals} per candidate — no wikitext)
  - research/35-s8-signals/README.text.md      (hit counts, rates, lift, examples with fiwiki diff links, and analysis)
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / "research" / "lib"))

import aot  # noqa: E402
import detect_hedging  # noqa: E402
import detect_loaded_terms  # noqa: E402
import detect_numbers  # noqa: E402

GROUP = "text"
DEFAULT_CANDIDATES = aot._CACHE.parent / "s8" / "candidates.jsonl"
OUT_JSONL = HERE / "results" / f"{GROUP}.jsonl"
OUT_README = HERE / f"README.{GROUP}.md"

DETECTORS = [
    ("number_changed", "detect_numbers.py", detect_numbers.detect),
    ("attribution_flip", "detect_hedging.py", detect_hedging.detect),
    ("loaded_term", "detect_loaded_terms.py", detect_loaded_terms.detect),
]

PAGE_TITLES: dict[int, str] = {
    440: "Jatkosota",
    1150: "Suomen sisällissota",
    1256: "Talvisota",
    1333: "Viipuri",
    6626: "Karjala",
    7675: "Suojeluskunta",
    8295: "Mainilan laukaukset",
    9186: "Punakaarti",
    9895: "Luovutettu Karjala",
    11398: "Risto Ryti",
    12374: "Käkisalmi",
    19086: "Karjala-kysymys",
    24094: "Suur-Suomi",
    24889: "Simo Häyhä",
    52097: "Moskovan rauhansopimus (1940)",
    105157: "Sotasyyllisyysoikeudenkäynti",
    250972: "Suomen sisällissodan nimet",
    319344: "Itä-Karjalan keskitysleirit",
}


def _fmt_lift(rate_rev: float, rate_ctl: float, rev_hits: int, n_ctl: int) -> str:
    if rate_ctl > 0:
        return f"{rate_rev / rate_ctl:.2f}×"
    if rev_hits == 0:
        return "0.00×"
    smoothed = rate_rev / (1.0 / n_ctl)
    return f"∞ (≥ {smoothed:.1f}× smoothed)"


NL = chr(10)
PIPE_ESC = chr(92) + "|"


def _md_escape(s: str) -> str:
    return " ".join(s.replace("|", PIPE_ESC).splitlines())


def _format_examples(examples: list[tuple[dict, list[dict]]], limit: int = 5) -> list[str]:
    if not examples:
        return ["- *No hits in this group.*"]
    lines: list[str] = []
    for cand, sigs in examples[:limit]:
        rid = int(cand["rev_id"])
        pid = int(cand["page_id"])
        title = PAGE_TITLES.get(pid, f"page {pid}")
        url = f"https://fi.wikipedia.org/w/index.php?diff={rid}"
        cls = f", `s1={cand['s1_class']}`" if cand.get("s1_class") else ""
        delta = f"{int(cand['bytes_delta']):+d} B"
        sig0 = sigs[0]
        extra = f" *(+{len(sigs) - 1} more in edit)*" if len(sigs) > 1 else ""
        lines.append(
            f"- [{rid}]({url}) (*{title}*, {delta}{cls}) — **{sig0['label']}**: `{_md_escape(sig0['evidence'])}`{extra}"
        )
    return lines


def build_readme(
    candidates: list[dict],
    per_detector_hits: dict[str, dict[str, list[tuple[dict, list[dict]]]]],
    any_hits: dict[str, int],
) -> str:
    n_rev = sum(1 for c in candidates if c["group"] == "reverted")
    n_ctl = sum(1 for c in candidates if c["group"] == "control")

    # Also compute unsourced-quantity sub-slice for number_changed
    unsourced_qty_rev = sum(
        1
        for _, sigs in per_detector_hits["number_changed"]["reverted"]
        if any(s["label"] == "Quantity changed without a new source" for s in sigs)
    )
    unsourced_qty_ctl = sum(
        1
        for _, sigs in per_detector_hits["number_changed"]["control"]
        if any(s["label"] == "Quantity changed without a new source" for s in sigs)
    )

    table_rows: list[str] = []
    for kind, mod_name, _ in DETECTORS:
        rev_list = per_detector_hits[kind]["reverted"]
        ctl_list = per_detector_hits[kind]["control"]
        r_cnt, c_cnt = len(rev_list), len(ctl_list)
        r_rate = r_cnt / n_rev if n_rev else 0.0
        c_rate = c_cnt / n_ctl if n_ctl else 0.0
        lift_str = _fmt_lift(r_rate, c_rate, r_cnt, n_ctl)
        table_rows.append(
            f"| `{kind}` (`{mod_name}`) | {r_cnt} / {n_rev} ({r_rate:.1%}) | {c_cnt} / {n_ctl} ({c_rate:.1%}) | **{lift_str}** |"
        )

    any_r = any_hits["reverted"]
    any_c = any_hits["control"]
    any_lift = _fmt_lift(any_r / n_rev, any_c / n_ctl, any_r, n_ctl)
    table_rows.append(
        f"| **Any `text` signal** | **{any_r} / {n_rev} ({any_r / n_rev:.1%})** | **{any_c} / {n_ctl} ({any_c / n_ctl:.1%})** | **{any_lift}** |"
    )

    sections: list[str] = []
    for kind, mod_name, _ in DETECTORS:
        rev_ex = _format_examples(per_detector_hits[kind]["reverted"], limit=5)
        ctl_ex = _format_examples(per_detector_hits[kind]["control"], limit=5)
        sections.append(
            NL.join(
                [
                    f"### `{kind}` (`{mod_name}`)",
                    "",
                    "**Reverted examples (up to 5):**",
                    *rev_ex,
                    "",
                    "**Control examples (up to 5):**",
                    *ctl_ex,
                ]
            )
        )

    uq_r_rate = unsourced_qty_rev / n_rev if n_rev else 0.0
    uq_c_rate = unsourced_qty_ctl / n_ctl if n_ctl else 0.0
    uq_lift = _fmt_lift(uq_r_rate, uq_c_rate, unsourced_qty_rev, n_ctl)

    return f"""# P1.S8 (#35) · Group `text`: Number, Hedging & Loaded-Term Detectors

Evaluated on the shared 300-edit candidate set (`$AOT_CACHE_DIR/s8/candidates.jsonl`: **{n_rev} reverted** vs **{n_ctl} non-reverted controls**, sampled with `seed=35` across the 18 core demo-topic articles).

## How to Run

```bash
# Unit tests (pure Python, no DB):
python3 -m unittest discover -s research/35-s8-signals

# Run all three text detectors over candidates.jsonl -> results/text.jsonl + README.text.md:
python3 research/35-s8-signals/run_text.py
```

## Summary Metrics

| Detector (`kind`) | Reverted hits (`n={n_rev}`) | Control hits (`n={n_ctl}`) | Lift (`rate_rev / rate_ctl`) |
|---|---:|---:|---:|
{chr(10).join(table_rows)}

Sub-slice of `number_changed` restricted to **`Quantity changed without a new source`**: **{unsourced_qty_rev} / {n_rev} ({uq_rate_pct(uq_r_rate)})** reverted vs **{unsourced_qty_ctl} / {n_ctl} ({uq_rate_pct(uq_c_rate)})** control (lift **{uq_lift}**).

---

## Example Hits by Detector

{chr(10).join(section + chr(10) for section in sections)}
---

## Finnish Diff Reading, False-Positive Patterns & Engineering Verdict

### 1. `loaded_term` (`detect_loaded_terms.py` + `loaded_pairs.json`) — **KEEP (high precision, direct UI highlight)**
- **What it catches:** Systematic ideological framing swaps that are tiny in byte delta (`-50 B` to `+130 B`) and easily missed by size ranking:
  - *Ceded vs. occupied Karelia & border irredentism:* `luovutetun Karjalan → Miehitetty Karjala` ([445526](https://fi.wikipedia.org/w/index.php?diff=445526)), `1940 luovutetut alueet → talvisodassa menetetyt alueet` ([13792608](https://fi.wikipedia.org/w/index.php?diff=13792608)), Viipuri's location swapped from `Luoteis-Venäjällä` to `Itä-Suomessa` ([20765131](https://fi.wikipedia.org/w/index.php?diff=20765131), `-12 B` with edit summary *"Korjattu kirjoitusvirhe"*) or `Venäjän tilapäisesti valloittamassa Karjalassa` ([21847143](https://fi.wikipedia.org/w/index.php?diff=21847143)), and headword `Priozersk → Käkisalmi` ([1578757](https://fi.wikipedia.org/w/index.php?diff=1578757)).
  - *1918 Civil War naming & legitimacy battles:* `sisällissota → vapaussota` across infobox, captions, and lead ([11596801](https://fi.wikipedia.org/w/index.php?diff=11596801), [11596668](https://fi.wikipedia.org/w/index.php?diff=11596668), [11000396](https://fi.wikipedia.org/w/index.php?diff=11000396)), `kapinoineen → vastustaneen` ([13768127](https://fi.wikipedia.org/w/index.php?diff=13768127)), `Valkokaarti → Valkoinen armeija` ([3289145](https://fi.wikipedia.org/w/index.php?diff=3289145), [3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)), `valkoisen senaatin ↔ Suomen tasavallan joukot` ([13797982](https://fi.wikipedia.org/w/index.php?diff=13797982), [13768127](https://fi.wikipedia.org/w/index.php?diff=13768127)), and `Venäjän → Miehittäjä-Ryssän` ([11000396](https://fi.wikipedia.org/w/index.php?diff=11000396)).
  - *Continuation War framing:* replacing `saksalainen / saksalaisjoukkojen / armeijan` with `natsien / natsijoukkojen / miehitysarmeijan` ([9225913](https://fi.wikipedia.org/w/index.php?diff=9225913)) and softening infobox outcome `Suomen tappio → Moskovan välirauha` ([2247824](https://fi.wikipedia.org/w/index.php?diff=2247824)).
- **False-positive patterns avoided:** Requiring a genuine directional swap (`a` removed and `b` added in the same aligned clause, rather than mere mention) prevents firing on historiographical name-list sections (*Suomen sisällissodan nimet*) or book titles in bibliographies (*Vapaussodan historian komitea*, `rev=3293415`). Control hit rate on the same 18 articles is **0 / 150 (0.0%)**.

### 2. `number_changed` (`detect_numbers.py`) — **KEEP `Quantity changed` (especially without new source); DOWNWEIGHT `Year changed`**
- **What it catches:** Silent in-place figure tampering that byte-size ranking completely buries:
  - [20345981](https://fi.wikipedia.org/w/index.php?diff=20345981) (*Punakaarti*, `+33 B`, `dispute`): inflated rifle shipments `15 000 → 25 000` and Russian fighters `3 000–4 000 → 10 000–14 000` with no new citation.
  - [4112248](https://fi.wikipedia.org/w/index.php?diff=4112248) (*Suur-Suomi*, `-75 B`): cut East Karelian camp internees `24 000 → 10 000` and camp deaths `6 000–9 000 → 4000` while breaking a closing `</ref>` tag (our token-novelty check on added references prevents broken `</ref>` merges from masquerading as a "new source").
  - [8109372](https://fi.wikipedia.org/w/index.php?diff=8109372) (*Simo Häyhä*): changed lead sniper kill count `542 → 259`, replacing *Kansallisbiografia (2003)* with wartime 1941–42 print sources.
- **Important wikidiff implementation detail:** Because Finnish uses spaces/`&nbsp;` as thousand separators (`15&nbsp;000` → `25&nbsp;000`), `wikidiff` leaves `&nbsp;000` unchanged and emits only `"15" → "25"` in `segments`. Reconstructing the full numeral token from `old`/`new` changed lines is essential so the UI shows `15 000 → 25 000` and `3 000–4 000 → 10 000–14 000` instead of truncated prefixes.
- **False-positive patterns observed in controls:**
  1. *Routine statistical updates with a new citation:* [14737751](https://fi.wikipedia.org/w/index.php?diff=14737751) updated Finland's state budget comparison `40 → 54` (billion €) while updating the Finance Ministry citation from 2006 to 2015. The `(with new source)` label separates this cleanly when the citation is genuinely new, though contested edits can also attach partisan/older sources ([8109372](https://fi.wikipedia.org/w/index.php?diff=8109372)) or add a citation in an unrelated paragraph ([3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)).
  2. *Year typo/fact fixes without a new source:* [202299](https://fi.wikipedia.org/w/index.php?diff=202299) fixed typo `vuoden 18 → vuoden 1918` and [795745](https://fi.wikipedia.org/w/index.php?diff=795745) fixed an infobox date error `1942 → 1941`. Splitting `Year changed` from `Quantity changed` isolates these routine date fixes.
  3. *Typography / bibliography / layout numbers (filtered out):* En-dash cleanup (`1939-1940 → 1939–1940`), thousand-space cleanup (`4000 → 4 000`), bibliography year parenthesization (`1978 → (1978)` in `rev=3293415`), and inline template widths (`{{Monta palstaa|75}} → 65` in `rev=24034423`) are suppressed.

### 3. `attribution_flip` (`detect_hedging.py`) — **DO NOT USE AS STANDALONE TRIGGER; keep only as a supplementary chip (`low weight`)**
- **Measured result:** **5 / 150 (3.3%)** reverted vs **10 / 150 (6.7%)** control (**0.50× lift** — *higher* in control than reverted).
- **Why lift is < 1× in Finnish Wikipedia (reading the diffs):**
  - Reverted hits are genuine POV edits: stripping interpretive hedging (`onkin tulkittu puhtaaksi kostotoimeksi → olivatkin puhdas kostotoimi` in [13234911](https://fi.wikipedia.org/w/index.php?diff=13234911) with summary *"ns. neutraali näkökulma on väistämättä venäläismielinen"*), adding scare quotes around Åland's `"avunpyynnöt"` ([3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)), stripping `joidenkin tietojen mukaan` and `Kansallisbiografian mukaan` ([8109372](https://fi.wikipedia.org/w/index.php?diff=8109372)), and flipping `välitti … käsityksen → osoitti` while inserting `Monet suomalaiset väittävät edelleen…` ([302362](https://fi.wikipedia.org/w/index.php?diff=302362)).
  - **However, constructive NPOV copyediting (`kielenhuolto`) uses the exact same lexical operations in reverse:** control editors routinely remove weasel words (`ns.` removed in [404544](https://fi.wikipedia.org/w/index.php?diff=404544) and [12384035](https://fi.wikipedia.org/w/index.php?diff=12384035); unattributed `Viimeisimpien tutkimusten mukaan` removed in [10523138](https://fi.wikipedia.org/w/index.php?diff=10523138); scare quotes `"tsaristiset"` removed as POV in [3360485](https://fi.wikipedia.org/w/index.php?diff=3360485)), add scholarly attribution (`Joidenkin historioitsijoiden mukaan` in [15372](https://fi.wikipedia.org/w/index.php?diff=15372)), or hedge an over-stated claim when a source does not support it (`myöntäneet → Kyseessä saattoi olla` in [14098576](https://fi.wikipedia.org/w/index.php?diff=14098576)).
- **False-positive traps already filtered out:** Finnish postposition `mukaan` idioms (`Kun mukaan lasketaan`, `mukaan lukien`, `Saksan mukaan lähteminen`), abbreviation expansion (`ns. → niin sanotun` in `rev=18237137`), quote-mark typography (`”…” → "..."`), and wholesale section deletions. Even after removing all mechanical noise, true semantic symmetry between POV hedging and NPOV copyediting keeps lift below 1×.
- **Verdict:** Keep `detect_hedging.py` **only** as an explanatory secondary signal chip on candidates already surfaced by revert/dispute, source, number, or loaded-term signals (e.g., on `rev=8109372` and `rev=3290065` where it pairs with `number_changed` / `loaded_term` to explain *how* the framing shifted), never as a primary ranker.

---

## Caveats

1. **18 demo articles only:** All 300 candidates (`150` reverted, `150` control) come exclusively from the 18 core articles of the top 4 demo topics (*1918 Civil War, Continuation War, Winter War, Ceded Karelia*). Rates and vocabulary do not yet cover contemporary politician biographies, NATO, Sámi history, or Swedish language politics.
2. **In-sample pair construction for `loaded_term`:** Per spec, `loaded_pairs.json` (16 pairs) was built from observed diffs in the reverted group. Its **0.0%** control false-positive rate on the same 18 articles is a fair test of precision against routine editing on those topics, whereas its **10.0%** reverted recall is an optimistic in-sample upper bound that will drop on unseen topics unless the pair list is extended or backed by LLM classification.
3. **Reverted ≠ manipulation, control ≠ neutral:** The `reverted` pool contains `66` `graffiti_cleanup` edits (blankings, slurs, keyboard mash) where subtle text signals *should* be 0, while `control` (non-reverted edits) includes surviving single-editor framing changes that nobody reverted.
"""


def uq_rate_pct(rate: float) -> str:
    return f"{rate:.1%}"


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--candidates", type=pathlib.Path, default=DEFAULT_CANDIDATES)
    args = ap.parse_args()

    if not args.candidates.exists():
        raise SystemExit(f"Candidates file not found: {args.candidates}")

    candidates = [json.loads(line) for line in args.candidates.read_text(encoding="utf-8").splitlines() if line.strip()]

    OUT_JSONL.parent.mkdir(parents=True, exist_ok=True)
    per_detector_hits: dict[str, dict[str, list[tuple[dict, list[dict]]]]] = {
        kind: {"reverted": [], "control": []} for kind, _, _ in DETECTORS
    }
    any_hits = {"reverted": 0, "control": 0}

    with OUT_JSONL.open("w", encoding="utf-8") as out_f:
        for cand in candidates:
            grp = cand["group"]
            combined_signals: list[dict] = []
            for kind, _, fn in DETECTORS:
                sigs = fn(cand)
                if sigs:
                    per_detector_hits[kind][grp].append((cand, sigs))
                    combined_signals.extend(sigs)
            if combined_signals:
                any_hits[grp] += 1
            row = {
                "rev_id": int(cand["rev_id"]),
                "group": grp,
                "signals": combined_signals,
            }
            out_f.write(json.dumps(row, ensure_ascii=False) + NL)

    readme_text = build_readme(candidates, per_detector_hits, any_hits)
    OUT_README.write_text(readme_text, encoding="utf-8")

    aot.log(f"wrote {len(candidates)} rows to {OUT_JSONL} and report to {OUT_README}")
    for kind, _, _ in DETECTORS:
        r_cnt = len(per_detector_hits[kind]["reverted"])
        c_cnt = len(per_detector_hits[kind]["control"])
        aot.log(f"  {kind:18s}: reverted={r_cnt:2d}  control={c_cnt:2d}")


if __name__ == "__main__":
    main()
