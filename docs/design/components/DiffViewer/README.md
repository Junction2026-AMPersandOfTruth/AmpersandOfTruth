# DiffViewer
Before/after of the affected passage. Added text is blue, and removed text is orange and struck through. The exact words a signal or the assessment cites get the evidence highlight.

- Toggles: **Inline / Side by side** (side by side hidden under 768 px), **Prose / Wikitext**, **EN / FI original**. The state lives in the query string (`?view=&text=&lang=`).
- Unchanged context folds to “⋯ N unchanged lines” with 1 line of context, like a code diff.
- Under the FI passage, the aligned EN machine translation keeps the same add/remove markup (P2.9 marker round-trip). If markers don't survive, it shows the plain translation and a note.
- **Security:** inserted text can contain anything. Render it only as text nodes, never as `MarkupString`.
