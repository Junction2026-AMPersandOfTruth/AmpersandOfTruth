# ArticleRailItem
A retrieved article in the overview's right rail: the title, why it matched, a 20-year edit sparkline and its incident count.

- The English title is shown when an en sibling exists. The Finnish title is always under it.
- The match reason comes from retrieval (P1.R6): “title match”, “section ‘Mainilan laukaukset’”, “similar text”.
- Clicking the title opens `/articles/{pageId}`. Clicking the count filters the incident list (`?article=`).
- The sparkline is `ArticleTimeline` with `Spark=true`.
