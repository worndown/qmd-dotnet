# Search Guide

qmd provides three search commands. Each trades speed for depth, and each is designed for a different type of query.

## Search modes at a glance

| Command | Alias | Method | Models required | Speed | Best for |
|---|---|---|---|---|---|
| `search` | — | BM25 keyword | None | Fast | Exact terms, known phrases |
| `vsearch` | `vector-search` | Vector cosine similarity | Embedding | Medium | Conceptual queries, synonyms |
| `query` | `deep-search` | BM25 + vector + RRF fusion | Embedding + reranker | Slowest | Best accuracy, complex questions |

---

## `qmd search` — Keyword Search

Uses SQLite FTS5 (BM25 ranking) for exact term matching. Fast, deterministic, and requires no models. Returns results only when query terms appear in the indexed documents.

```
qmd search <query> [options]

Options:
  -n, --limit <N>           Max results [default: 5]
  -c, --collection <name>   Filter by collection (repeatable)
  --min-score <score>       Minimum BM25 relevance score [default: 0]
  --all                     Return all results, ignoring --limit
  --format <format>         Output format [default: cli]
  --full                    Show full document content
  --line-numbers            Prefix lines with line numbers
```

**When to use:** You know the exact terms that appear in your documents — function names, error messages, specific phrases. If the terms aren't in your corpus, `search` returns nothing.

**Example:**
```bash
qmd search "reciprocal rank fusion" --limit 10
qmd search "getUser" --collection api-docs --format json
```

---

## `qmd vsearch` — Semantic Search

Finds documents by meaning using an embedding model (embeddinggemma-300M) and cosine similarity via sqlite-vec. Generates alternative phrasings via query expansion to improve recall.

```
qmd vsearch <query> [options]

Options:
  -n, --limit <N>           Max results [default: 10]
  -c, --collection <name>   Filter by collection (repeatable)
  --min-score <score>       Minimum cosine similarity score [default: 0.5]
  --intent <text>           Domain context hint for query expansion
  --all                     Return all results, ignoring --limit
  --format <format>         Output format [default: cli]
  --full                    Show full document content
  --line-numbers            Prefix lines with line numbers
```

**When to use:** Conceptual queries, synonyms, "how do I..." questions, or when you don't know the exact terminology used in your documents.

**`--intent`:** Provides domain context to the query expansion model. Helps when your query is ambiguous or the vocabulary in your documents differs from your query.
```bash
qmd vsearch "handling errors" --intent "Python exception handling"
```

**`--min-score`:** Cosine similarity ranges from 0 to 1. The default of 0.5 is set above the noise floor for the default embedding model. Lower values return more results but increase false positives. Use `qmd profile-embeddings` to find the right threshold for your corpus.

**Example:**
```bash
qmd vsearch "how to structure REST endpoints" --limit 5
qmd vsearch "deployment strategies" --intent "Kubernetes" --min-score 0.6
```

---

## `qmd query` — Hybrid Search

Combines keyword search (BM25) and vector search through RRF fusion, then refines results with an LLM reranker (Qwen3-Reranker-0.6B). Includes query expansion and multiple relevance safeguards. Most accurate, but slowest due to LLM inference.

```
qmd query <query> [options]

Options:
  -n, --limit <N>               Max results [default: 10]
  -c, --collection <name>       Filter by collection (repeatable)
  --min-score <score>           Minimum relevance score [default: 0.2]
  --intent <text>               Domain context hint for query expansion
  --no-rerank                   Skip LLM reranking (faster, raw RRF scores)
  -C, --candidate-limit <N>     Max candidates passed to reranker [default: 40]
  --chunk-strategy <strategy>   Chunking strategy: regex or auto [default: regex]
  --explain                     Show per-document retrieval traces
  --all                         Return all results, ignoring --limit
  --format <format>             Output format [default: cli]
  --full                        Show full document content
  --line-numbers                Prefix lines with line numbers
```

Alias: `deep-search`

**When to use:** When accuracy matters most and you can afford to wait a few seconds. Good for complex questions, cross-topic queries, and situations where keyword search returns too many or too few results.

**`--no-rerank`:** Skips the LLM reranker. Results are ranked by raw RRF score instead. Faster, but less accurate. Useful when the reranker model is not downloaded or you need quick results.

**`--explain`:** Prints a per-document breakdown showing the BM25 score, vector cosine score, RRF contribution, and reranker score. Useful for diagnosing unexpected results.

**`--chunk-strategy auto`:** Enables AST-aware chunking for code files (C, C++, C#, Go, JavaScript, Python, Rust, TypeScript). Produces better semantic boundaries for source code. The `regex` default works well for prose documents.

**`--candidate-limit`:** Controls how many RRF-ranked candidates are passed to the LLM reranker. Higher values improve recall at the cost of reranking time.

**Example:**
```bash
qmd query "consistency vs availability in distributed systems"
qmd query "database migration strategy" --explain --limit 5
qmd query "async error handling" --no-rerank --collection python-docs
```

---

## Output formats

All search commands support the same output formats via `--format` or shorthand flags:

| Format | Shorthand | Description |
|---|---|---|
| `cli` | (default) | Human-readable terminal output with document snippets |
| `json` | `--json` | JSON array of result objects |
| `csv` | `--csv` | Comma-separated values |
| `md` | `--md` | Markdown-formatted results |
| `xml` | `--xml` | XML elements |
| `files` | `--files` | File paths only, one per line |

The shorthand flags (`--json`, `--csv`, etc.) are aliases for `--format <format>`.

**Example:**
```bash
qmd search "deployment" --json | jq '.[].path'
qmd query "CI pipeline" --files | xargs grep -l "docker"
```

---

## Tuning `--min-score`

Score semantics differ by command:

- **`search`** — BM25 scores are unbounded (higher is more relevant). The default of 0 means no filter. Raise it to eliminate low-quality matches.
- **`vsearch`** — Cosine similarity in [0, 1]. Default is 0.5, which is above the noise floor for the default embedding model. See note on anisotropy below.
- **`query`** — Blended RRF/reranker score. Default is 0.2. This mode has additional built-in safeguards on top of `--min-score`.

See [Calibrating Search Thresholds](profile-embeddings.md) for a step-by-step guide to running `qmd profile-embeddings` and interpreting the output.

---

## Safeguards against false positives

The `query` command has five layers of protection that activate when BM25 returns no results and only vector search contributes to the fusion:

1. **Vector-score gate** — Returns empty if all vector scores are below 0.55
2. **Reranker gate** — Returns empty if the best reranker score is below 0.1
3. **Score cap** — Clamps blended scores to the best raw cosine similarity
4. **Confidence gap filter** — Drops results scoring below 50% of the top result
5. **Raised defaults** — `vsearch --min-score 0.5`, `query --min-score 0.2`

These exist because embedding models produce a **noise floor**: unrelated documents in the same language share roughly 0.45 cosine similarity due to structural patterns in the embedding space (anisotropy). Without safeguards, purely vector-driven results can surface documents that are not genuinely relevant.

For the full explanation of RRF mechanics, score blending, and the research behind these defaults, see [Hybrid Search Internals](hybrid-search-guide.md).

---

## Limitations

qmd runs entirely locally with small models — embeddinggemma at 300M parameters for embeddings and Qwen3-Reranker at 0.6B parameters for reranking — backed by sqlite-vec for vector storage. It works well for personal and project document collections, but it is not comparable to cloud search services powered by much larger models and purpose-built vector databases.

- **`search`** returns nothing if query terms are absent from the corpus — there is no fuzzy or stemming fallback
- **`vsearch`** can return false positives at low `--min-score` thresholds due to embedding anisotropy
- **`query`** is the most accurate mode, but LLM inference adds latency (a few seconds per query on CPU)
- All models run locally; quality depends on the embedding and reranker models available
