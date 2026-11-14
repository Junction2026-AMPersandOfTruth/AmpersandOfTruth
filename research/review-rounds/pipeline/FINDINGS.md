# Pipeline-mode rehearsal (Investigations:Source=Pipeline), 2026-11-14

Walk against a local app on the real pipeline (#102 + #40, before #117/#120): 19/20 steps ok desktop + phone, 0 console errors,
0 failed requests, no horizontal scroll. Cold investigation 9–19 s desktop, cached replay ~2.6 s. Screens were reviewed by hand
(the Gemini worker could not read git-ignored screenshots).

| id | severity | what | where it goes |
|---|---|---|---|
| PM-01 | hurts comprehension | A number change ("1 000–1 500" → "n. 2000") is headed "Removed: …": `DeriveKind` calls it a deletion because the removed text is longer | `p2-fixincident` (IncidentMapper.DeriveKind: number change first) |
| PM-02 | hurts comprehension | Assessment quotes show raw wikitext and entities (`&nbsp;`, `\| tappiot2 = …`) | `p2-fixincident` (decode entities, strip markup for display) |
| PM-03 | blocks demo (pipeline) | "Civil War 1918" retrieves Vilho Lehokas, Quebecin taistelu (1759)… and not *Suomen sisällissota*; 9 of 10 rail articles have 0 incidents | P1: R6 / PR #117 |
| PM-04 | polish | 2 of 9 incidents have no claim → "Text added to Battle of Tampere" (fallback works, reads thin) | P1: S9/S10 claim extraction |
| PM-05 | polish | Related cards in the side column are cramped (article name wraps into 4 lines) | `p2-fixoverview` (IncidentCard Compact) |
| PM-06 | hurts comprehension | EN diff and edit summaries show "Loading translation…" on a cold incident | `p2-fixincident` (R1-01) |
