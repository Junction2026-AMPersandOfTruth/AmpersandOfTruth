# StatusBanner
Page-level notices for dependency trouble: info (cached), warn (degraded) and error (failed). The human sentence comes first, and technical detail sits behind a disclosure.

- **Warn** (`warn-bg`): the LLM is unavailable. Results are capped at “Removed by Wikipedia editors”.
- **Error** (`error` icon only, on `error-bg`): corpus or embedder down, or a run failed. Always offers *Retry*.
- Red is used **only** here, never on incident content.
