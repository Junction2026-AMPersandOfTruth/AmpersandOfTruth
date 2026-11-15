# Ampersand of Truth

**See what was added to Wikipedia — and what the community later removed.** Live: https://asot.tech

Ampersand of Truth is an investigative tool for understanding how information about the history of Finland changes on Wikipedia. It uncovers disputed edits and patterns consistent with coordinated attempts to influence historical narratives, using evidence from Wikipedia's revision history.

## The Problem

Wikipedia's open editing model makes it possible to introduce misleading claims, reshape historical narratives and repeatedly reintroduce disputed content. The revision history records all of it, but investigating thousands of Finnish revisions — what changed, who changed it, how long it stayed visible and why it was removed — takes enormous effort.

## Our Solution

Users enter a topic. The app finds relevant Finnish Wikipedia articles on the history of Finland, reads their edit history and shows substantive content that other editors later removed. For each finding, users see:
- **What was added:** the inserted text, its context and an English reading of the claim.
- **Who added it:** the Wikipedia account, with IP addresses always masked.
- **When:** insertion and removal dates, and how long the content stayed visible.
- **What happened next:** whether it was put back, removed again or fought over.
- **Why it matters:** the signals found, explained in plain words.
- **How strong the evidence is:** one of three clearly defined levels.

Every finding links to the exact revisions on Wikipedia, so anyone can verify it. Users can also trace a claim on the web, to see whether it appeared elsewhere before, during or after its time on Wikipedia, and whether later pages likely copied it from Wikipedia.

## Evidence-Based Analysis

Removed content is not automatically false, and revision history cannot prove an editor's intentions. Findings are therefore given one of three levels:

1. **Removed by Wikipedia editors:** substantive content was added and later removed by a different editor.
2. **Disputed and removed:** the content is also a claim the community disputed — for example, it was removed for accuracy or sourcing reasons, or put back and removed again.
3. **Pattern consistent with a coordinated attempt:** several independent signals together, such as the same claim reinserted by multiple editors over time alongside changed or removed sources.

AI helps read the Finnish text and recognise claims, but it cannot raise a finding's level on its own: that always requires independent evidence from the revision history. The AI does not judge whether a claim is true, and it never sees usernames or IP addresses. No editor is scored or labelled. When in doubt, the lower level is used — overclaiming is treated as a harm.

## How It Works

1. **Topic discovery:** an LLM expands the query (Finnish titles, reformulations), then bge-m3 vector searches find the relevant articles.
2. **Revision analysis:** the system ranks every revert in those articles and reads the most promising removed edits, skipping bots, self-removals, markup-only changes and content that is still in the article.
3. **AI-assisted classification:** Gemini reads each removed edit, states the claim in English and quotes the evidence, which is verified against the original text.
4. **Evidence evaluation:** deterministic rules combine the assessment with revision-history signals (re-insertions, edit wars, removal reasons, source changes) to assign the level.
5. **Exploration:** an interactive interface with incident pages, article timelines, account summaries, an English/Finnish toggle and a methodology page that states the tool's limits openly.

## Technology

- **Blazor and .NET 10:** interactive web application.
- **Entity Framework Core and SQLite:** application state and caches.
- **MariaDB:** Wikipedia revision data and vector search.
- **Ollama and bge-m3:** semantic embeddings for topic discovery.
- **Google Gemini:** query expansion, classification, translation and web-claim matching.
- **Oxylabs:** web search for tracing claims beyond Wikipedia.

The dataset contains 862,076 revisions across 19,742 Finnish Wikipedia articles on the history of Finland, from 2003 to October 2026.

## Who Benefits?

Journalists, researchers, educators, fact-checkers and anyone interested in how historical narratives are edited and contested. Our goal is to make information manipulation easier to investigate, while keeping every conclusion transparent, traceable and grounded in verifiable evidence.

**Ampersand of Truth: understand not just what Wikipedia says today, but how it got there.**
