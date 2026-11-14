# Golden set: human review (P1.E1, #20)

**How:** for each item, open the *added* and *removed* links (fi.wikipedia diffs), read them against
`docs/evidence-standard.md` §1, then decide. Labellers' doubts per topic: `LABELLER-NOTES.md` (same folder).
**Record decisions** either in chat (e.g. "winter-war: all B agree; i06 → contested") and Claude writes
`human.json`, or directly in `human.json`: `{"<item id>": {"label": "contested", "verified": true, "by": "human:<initials>"}}`
(`"exclusion"` for not-incident, `"drop": true` to remove an item). Then `python3 research/20-golden/merge.py`.

- **A** = labellers disagree and no policy applies; the draft keeps the lowest level. Pick one.
- **B** = labellers agree on contested/campaign; confirm (or demote) before sign-off.

Counts: {"topics": 13, "test_topics": 4, "controls_quiet": 1, "controls_out_of_corpus": 2, "queries": 62, "articles": 256, "incidents": 195, "non_incidents": 134, "label:not-incident": 134, "label:reverted": 106, "label:contested": 84, "label:campaign": 5, "unverified_contested_or_campaign": 0}
Agreement: {"agree": 250, "disagree": 79, "no_blind": 0, "agree_claude:blind": 172, "n_claude:blind": 201, "agree_gemini:blind": 271, "n_gemini:blind": 329, "spot_check_accepted": 70, "labeller_default": 62}
Topics not labelled yet: none

## Contents
