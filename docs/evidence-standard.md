# Evidence standard: what the app may claim, and how editors are shown

Decision for P0.7 (#18), 2026-11-14. **Binding for the pipeline (P1.S9, P1.S10), the golden set (P1.E1), all UI copy (P2) and the pitch.**
Code: `src/AmpersandOfTruth.Web/Features/Evidence/`. Any change to this file must update that code and its tests in the same commit, and the reverse.

**The core rule:** edit history proves *what* was written, *by which account*, *when*, and *that it was taken out*. It does not prove intent or falsehood.
The app reports what the community did. It never calls a person or account anything.

## 1. Evidence levels

Every incident has exactly one level. Levels only add evidence: each level requires everything in the level below it.

| Level | Id (machine, never shown) | UI label (exact) | Short label (chips, legends) |
|---|---|---|---|
| 1 | `reverted` | **Removed by Wikipedia editors** | Removed |
| 2 | `contested` | **Disputed and removed** | Disputed |
| 3 | `campaign` | **Pattern consistent with a coordinated attempt** | Possible coordination |

The ids match the CSS tokens `--tier-reverted-*`, `--tier-contested-*` and `--tier-campaign-*` (P2.2), the golden-set labels and the pipeline JSON.
UI copy says "level", never "tier".

### What each level requires

**Not an incident: never shown.** Any one of these excludes the item:
- **Not substantive.** The edit changed only markup, whitespace, categories, interwiki links or templates. It added no prose, number, date, name or citation.
- **Self-removal.** The same account that added the content took it out again.
- **Bot.** The content was added by a bot account.
- **Blanking.** The edit blanked the page or a whole section.
- **Kept.** Someone undid the removal and the content is still in the latest revision. In that case the community kept it.
- **Graffiti.** The automated assessment classed it as `vandalism` or `test/nonsense` with confidence ≥ 3. Below that, the item stays at Level 1.

**Level 1 · Removed by Wikipedia editors.** Substantive content was added and later taken out by a *different* account, and no exclusion applies.
This is literally true of every item at this level, so it is also where everything lands when the automated assessment is missing, failed or over budget.

**Level 2 · Disputed and removed.** Level 1, plus **both**:
1. **It is a claim.** The automated assessment (P1.S9) classes it as one of `content dispute`, `POV/narrative push`, `false factual claim` or `source manipulation`. Its confidence must be ≥ 3 of 5, and at least one of its quoted evidence spans must be found verbatim in the inserted text.
2. **The community disputed it.** At least one of these:
   - the removal reason (P1.S5) is about accuracy, neutrality or sourcing;
   - the content was put back at least once after a removal;
   - it was part of a back-and-forth over the passage (edit war, P1.S4);
   - a dispute template (`{{Neutraalius}}`, `{{Lähteetön}}`, `{{Korjattava}}`, …) was added in the same episode.

**Level 3 · Pattern consistent with a coordinated attempt.** Level 2, plus at least **two** of these four signals. At least one of the two must be about accounts or articles:
- **Accounts:** the same claim (P1.S7 cluster) was added by ≥ 2 distinct editors. IP addresses in the same masked range count as **one** editor (see §3).
- **Articles:** the same claim was added to ≥ 2 articles.
- **Persistence:** the claim was put back ≥ 2 times, with ≥ 7 days between its first and last insertion.
- **Sources:** source manipulation in the same incident: a citation was removed, replaced or made up alongside the claim (P1.S8).

One account pushing one article, however persistently, stays at Level 2. "Coordinated" needs more than one place or more than one editor.

**Thresholds** (3/5 confidence, 2 editors, 2 articles, 2 re-insertions, 7 days, 2 signals) are defaults in `TierThresholds`. P1.S10 may tune them against the golden dev split and records any change here.
The **structure** is frozen: which signals exist, which level they feed, and the asymmetry below.

### The asymmetry rule

- The automated assessment can **exclude or demote** on its own. Graffiti is excluded; a `good-faith error` or `other` classification is capped at Level 1.
- It can **never promote** on its own. Reaching Level 2 also needs a community-dispute signal from the edit history and a verified quote.
- Structural signals can **never promote** without the content check either. An edit war over formatting is not a disputed claim.
- When in doubt, assign the lower level. Underclaiming is a quality problem; overclaiming is a harm.

### Why-flagged sentence

Each level explains itself as `<label> — because: <reason>, <reason>.` The reasons are the signals that met the bar, in plain words with no verbs of intent. For example:

> Disputed and removed — because: removed as not neutral, put back 2 times after removal, automated assessment: a factual or narrative claim.

The canonical phrases live in `TierRules`. P2.10's signal phrasebook may reword them, but the copy rules below still apply.

## 2. Wording rules (UI, exports, pitch)

- **Never about a person or account:** propagandist, disinformation agent, troll, sockpuppet, bad actor, liar, paid editor, fake account, bot farm, guilty, malicious.
  "Propaganda" and "disinformation" may describe the *problem space* ("Wikipedia faces disinformation attempts") or a historical subject. They never label a specific edit, claim or account.
- **No certainty we don't have:** never "proven", "confirmed false", "fake". Say "removed", "disputed", "consistent with".
- **No verbs of intent:** not "tried to", "deliberately", "intentionally", "in bad faith". Edits *added*, *removed* and *put back*.
- **Jargon stays out of UI copy:** no "revert", "sha1", "tier" or "LLM". Write "removed", "level" and "automated assessment".
- **Model output is an assessment, never a fact.** It is always labelled "Automated assessment" and shown next to the quoted text it relies on. It never says whether a claim is true. A claim that contradicts well-established history goes in a separate, labelled field (P1.S9 guardrail).
- `CopyRules` enforces the account and certainty terms in tests over every `.razor` file and every string in `Features/Evidence`.

## 3. How editors are shown

Corpus profile (`research/p0.7-evidence-standard/`, 862,076 revisions):

| Editor kind | Revisions | Share | Distinct editors |
|---|---:|---:|---:|
| Named account | 695,702 | 80.7% | 15,360 |
| IPv4 | 151,419 | 17.6% | 51,056 |
| IPv6 | 11,709 | 1.4% | 5,245 |
| Temporary account (`~2025-26183-73`, since 2025-09-10) | 3,228 | 0.4% | 1,769 |
| Hidden by Wikipedia (no username) | 18 | <0.01% | — |

*Named includes 72,727 revisions by 179 bot-like names and one 2017 account whose name happens to start with `~`.*

| Kind | Shown as | Linked to |
|---|---|---|
| Named account | the name exactly as on Wikipedia | its public contributions page on fi.wikipedia.org |
| Temporary account | the name as on Wikipedia + "(temporary account)" | its public contributions page |
| IPv4 | first two octets, `85.76.x.x`, + "(anonymous)" | nothing. Only the edit row links to the revision. |
| IPv6 | first two groups, `2001:14BB:…`, + "(anonymous)" | nothing |
| Hidden | "Hidden editor" | nothing |

Rules:
- **Every edit row links to its public revision diff** (`https://fi.wikipedia.org/w/index.php?diff=prev&oldid=<rev_id>`). Evidence is always one click from Wikipedia itself.
- **No per-editor scores, ranks or adjectives** in the UI: no "suspicion", no "most reverted accounts" leaderboard, no editor names in page titles, headlines or share text.
  Account descriptions state observable facts only ("12 edits in this dataset, 9 removed by others, all on 2 articles"), next to the caveat "Only edits within our dataset of 19,742 articles are shown."
- **No pages or search by IP.** `/accounts/{name}` (P2.8) exists only for named and temporary accounts.
- **Counting editors uses the same masking.** IPs in one masked range (IPv4 /16, IPv6 /32) count as one editor for Level 3 (`EditorLabel.GroupingKey`). Shared and reassigned addresses would otherwise fake "multiple accounts". This errs low, on purpose.
- **Model prompts never contain usernames or IPs.** Editors become "Editor 1", "Editor 2", … plus their kind (named, anonymous, temporary). This keeps personal data out of the third-party API and keeps names from biasing the assessment.
- Storage and pipeline keep the raw `user` string. It is public data and is needed for clustering. Masking is a display rule applied in exactly one place (`EditorDisplay`) and in every export.

## 4. Golden set (P1.E1): labels and what counts as a true positive

Each golden item carries `label` ∈ `campaign | contested | reverted | not-incident`. That is the level a human assigns after reading the full evidence against §1.
`not-incident` items also carry `exclusion` ∈ `not-substantive | self-removal | bot | blanking | kept | graffiti`. Record *why* for every label.

**Matching.** A system incident matches a golden item when the incident's inserting `rev_id`s intersect the item's inserting `rev_id`s; for multi-article items any member article counts. Each golden item is matched at most once, by the highest-ranked system incident; further matches are **duplicates**.

| Metric | Definition |
|---|---|
| True positive | a shown incident matching a golden `contested` or `campaign` item |
| False positive | a shown incident matching a `not-incident` item, or a duplicate |
| Strict precision@k | relevant = golden `contested`/`campaign`. **Decisions use this one.** |
| Lenient precision@k | relevant = golden `reverted`/`contested`/`campaign` |
| Recall | over golden `contested` + `campaign` items |
| Level accuracy | exact level match on matched items |
| **Overclaim rate** | share of matched items shown *above* their golden level (a `not-incident` shown at any level counts). Reported for every variant. Between variants with similar recall, the one with fewer overclaims wins. |

Shown incidents with no golden item are labelled by the P1.E3 judge using this document as its rubric.

## 5. Attribution and methodology text

The exact strings live in `Features/Evidence/Attribution.cs` and `Methodology.cs`. They are used by the footer (P2.2), `/methodology` and "why flagged" (P2.10), and exports (P4.3).

- **Footer** (every page): *Wikipedia text © its contributors, CC BY-SA 4.0, quoted from the fi.wikipedia.org revisions linked on each passage. Data: Wikimedia dumps, FineWiki, MariaDB Foundation datasets; embeddings: bge-m3. Translations and assessments are automated and can be wrong.* + a "How this works" link to `/methodology`.
- **Per passage:** *fi.wikipedia.org revision 123456, 2014-03-02 18:20 UTC, by &lt;editor label&gt;. CC BY-SA 4.0.*, linked to the revision.
- **Machine translation** of Wikipedia text is an adaptation, so it is shared under CC BY-SA 4.0 too and always tagged. The tag also satisfies CC BY-SA's "indicate if changes were made".
- **Methodology summary:** Ampersand of Truth searches the edit history of Finnish Wikipedia articles about the history of Finland for text that was added and later taken out by other editors. For each finding it shows what was added, when, by which account, how long it stayed visible and how it was removed, with a link to every revision on Wikipedia. Each finding gets one of three levels based only on that history. Automated assessments help read the Finnish text and recognise factual claims. They are labelled as such and shown next to the quoted text they rely on.
- **Limitations** (shown on `/methodology`; also in the pitch Q&A):
  1. Removed does not mean false, and kept does not mean true. We only see what Wikipedia's editors caught.
  2. Edit history cannot show intent. A pattern consistent with coordination can also be one persistent person, a shared network, or several people who honestly share a view.
  3. We only see our dataset: 19,742 Finnish Wikipedia articles in the History of Finland category tree, 2003 to 1 October 2026. Activity elsewhere on Wikipedia or the web is invisible.
  4. One IP address can be many people, and one person can use many addresses. We mask addresses and count each masked range as one editor.
  5. Translations and automated assessments can be wrong. The original Finnish is always one click away.
  6. Measured quality on our hand-checked test set: *filled in from P1.S11*.
