# TopicSearch
The entry point: one big input, suggested-topic chips, the scope line and the three-step strip. A compact variant lives in the app bar.

- The placeholder is the question: “What topic do you want to investigate?” Enter submits, and any language is accepted.
- `EditForm FormName="topic-search"`, trimmed, 2–120 characters. Empty submit does nothing (no error).
- The chips are the demo topics plus known-good ones. **They matter most on this screen**, because the corpus only covers Finnish history.
- Filters (date range, minimum level) sit in a collapsed `<details>`.
