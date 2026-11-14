# Known limitations (P1.P3 #41 quality gate → methodology page P2.10)

What the app can and cannot tell a user, stated plainly. Every number comes from the golden set (`research/eval/golden.json`,
version `2026-11-14.1`): either the dev split or the sealed test split. The decision records are #27 (retrieval) and
#38 (`research/38-s11-decision/README.md`, detection). Wording rules are in `docs/evidence-standard.md`. When a number
here changes, update this file in the same commit.

## What the data covers
- **Only 19,742 Finnish Wikipedia articles.** These are the `Suomen historia` category tree plus 15 political and
  historical pages. The edit history runs to 2026-10-01. Edits on other articles, other wikis, talk pages and deleted
  revisions are not seen.
- **Only edits that someone removed.** The app finds content that was added and later taken out by another editor.
  Content that is still in the article, or that nobody reverted, is invisible to it, however wrong it is.

## How incidents are found, and what that misses
- **Exact reverts only.** The pipeline starts from *identity reverts*: an edit that restored an exact earlier version of
  the page. A claim removed by rewording or partial editing is not shown. In the golden set, about 1 in 7 incidents was
  removed that way. A tracking method finds 13 of 20 such cases in research, but it is not in the app yet.
- **About half of the known incidents in the top 50.** On the held-out test topics, the shipped configuration lists
  50 % of the hand-labelled contested/campaign incidents (60 % on the development topics). Caps and filters drop the
  rest: at most 5 incidents per article, and a limited number of candidate edits read per topic.
- **The topic search misses some articles.** On the test topics, 45 % of the relevant articles are in the top 20
  results (recall@20). Vague phrasings do worse than precise Finnish ones. An "outside this collection" warning comes
  from an AI check that was tested on only 3 out-of-scope topics, so it is a warning, never a block.

## How sure the labels are
- **Top results are mostly right, levels are not always.** On the held-out test topics, 85 % of the top 10 incidents
  are contested or campaign-level by the human labels and the independent AI judge. But 33 % of the matched incidents
  are shown **above** the level a human gave them (16 % on development topics). The usual causes are long disputes
  between regular editors shown as "consistent with a coordinated attempt", and content disputes shown as "disputed"
  where the labeller saw a good-faith edit.
- **Level 3 never claims coordination.** It says the pattern is *consistent with* a coordinated attempt: several
  accounts or articles, persistence, source changes. Several accounts can be one person, and one account can be
  several people. Anonymous editors in one network range (IPv4 /16, IPv6 /32) are counted as one editor. This
  undercounts on purpose.
- **The content reading is a language model's.** A Gemini model reads the Finnish wikitext, names the category and
  states the claim in English. It agrees with human category labels about two times in three. Its quotes are checked
  against the text, and an incident without a verified quote cannot reach level 2. It does **not** judge whether a
  claim is true. Where it notes that a claim contradicts well-established history, that note is shown separately.
- **The benchmark is small.** 13 labelled topics, 329 items, labelled by AI labellers with human sign-off on every
  disputed or campaign item. Only 5 campaign items exist (1 in development, 4 in test), so the level-3 thresholds are
  indicative.

## People and privacy
- Named accounts are shown by their public Wikipedia name and linked to their public contributions. IP addresses are
  always masked. Edit summaries are shown with every IP masked.
- No editor gets a score, a rank or a label (no "troll", no "propagandist"). An account page states counts only, within
  this dataset.
- The AI model never sees usernames or IPs; editors are "Editor 1", "Editor 2", ….

## Speed
- First incident in under 15 s and a complete result in under 45 s when nothing is cached (median; slow corpus links have
  taken up to 33 s / 48 s). Under 2 s when the topic has been run before. Demo topics are pre-warmed (`scripts/warm.sh`).
