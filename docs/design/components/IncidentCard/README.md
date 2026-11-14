# IncidentCard
One flagged incident in the overview list: tier, the English claim as the headline, a one-line story, key facts and signal chips. The whole card links to the incident page.

- Order is fixed: tier + article → claim (serif, quoted) → FI original → “what happened” sentence → facts (First seen · Last removed · Live · Accounts · Articles) → chips (at most 4, plus “+2 more”).
- The “what happened” sentence is generated from counts: *Inserted {n} times by {k} accounts over {span}; live for {d} in total.*
- `is-highlighted` when its mark is hovered on the timeline strip, and vice versa.
- While scoring, a skeleton card holds its place, so the list doesn't jump.
- Deletion incidents read “Removed: …” as the headline.
