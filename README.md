# qmd-dotnet

An unofficial .NET port of [qmd](https://github.com/tobi/qmd) by [Tobi Lutke](https://github.com/tobi) — a local, on-device search engine for markdown documents.

This is not a fork. It is written from scratch in C# and reproduces the functionality of the original TypeScript tool. Based on **qmd version 2.1.0**.

## Platform

**Windows only.** This port was built for personal use and does not include Linux or macOS support. If you'd like to add support for other platforms, contributions are welcome.

## Features

- **Hybrid search** — BM25 full-text search, vector semantic search, and hybrid mode with reciprocal rank fusion
- **Local LLM inference** — uses [LLamaSharp](https://github.com/SciSharp/LLamaSharp) (llama.cpp) for embeddings, re-ranking, and query expansion
- **CUDA and CPU** — GPU-accelerated via CUDA 12, with automatic CPU fallback
- **MCP server** — exposes search tools via Model Context Protocol for AI assistant integration
- **Multiple output formats** — CLI, JSON, CSV, Markdown, XML

## Search Commands

QMD provides three search commands. Each trades speed for depth.

### `search` — Keyword Search

Uses BM25 full-text search (SQLite FTS5) for exact term matching. Fast and precise — returns results only when query terms appear in the documents. Returns nothing when terms are absent from the corpus.

```bash
qmd search "API versioning" [--limit N] [--min-score N] [--collection C]
```

### `vsearch` — Semantic Search

Finds documents by meaning using an embedding model (embeddinggemma-300M) and cosine similarity via sqlite-vec. Supports LLM-powered query expansion to generate alternative phrasings. Good for conceptual queries, synonyms, and "how do I..." questions.

```bash
qmd vsearch "how to structure endpoints" [--limit N] [--min-score 0.5] [--intent "..."] [--collection C]
```

Default `--min-score` is **0.5**, set above the noise floor for the default embedding model.

### `query` — Hybrid Search

Combines keyword and vector search through RRF fusion, then refines results with an LLM reranker (Qwen3-Reranker-0.6B). Includes query expansion, chunk-level matching, and multiple relevance safeguards. Most accurate, but slowest due to LLM inference.

```bash
qmd query "consistency vs availability" [--limit N] [--min-score 0.2] [--no-rerank] [--explain] [--collection C]
```

Default `--min-score` is **0.2**. Use `--no-rerank` to skip the LLM reranker for faster results. Use `--explain` to see per-document scoring breakdowns.

## How Scoring Works

### Fusion (RRF)

The `query` command uses Reciprocal Rank Fusion ([Cormack et al., SIGIR 2009](https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf)) to merge ranked lists from keyword and vector search:

```
contribution = weight / (k + rank + 1)     k = 60, rank is 0-indexed
```

A top-rank bonus is added: **+0.05** for rank 1, **+0.02** for ranks 2-3. Documents appearing in multiple lists accumulate contributions from each.

### Weights

The first two ranked lists (typically the original BM25 results and the first query expansion variant) receive **2x weight**. Remaining lists from further query expansions receive **1x weight**. Weights are currently fixed and not configurable via CLI.

### Score Blending

After RRF ranking, top candidates are re-scored by the LLM reranker. The final score blends position with the reranker's relevance judgment:

```
rrfWeight = 0.75 (ranks 1-3) | 0.60 (ranks 4-10) | 0.40 (ranks 11+)
finalScore = rrfWeight * (1 / rrfRank) + (1 - rrfWeight) * rerankScore
```

Top-ranked results lean more on their RRF position; lower-ranked results lean more on the reranker.

## Limitations and Expectations

QMD runs entirely on-device with small models — embeddinggemma at 300M parameters for embeddings and Qwen3-Reranker at 0.6B parameters for reranking — backed by sqlite-vec for vector storage. It works well for searching personal and project document collections, but it is not comparable to cloud search services powered by trillion-parameter models and purpose-built vector databases.

### Why false positives can occur

Embedding models produce a **noise floor**: unrelated documents in the same language typically share ~0.45 cosine similarity due to structural patterns in the embedding space (a well-documented phenomenon called anisotropy). When the `query` command receives no keyword matches, only vector search contributes to the fusion, and RRF's purely positional scoring can inflate results that aren't genuinely relevant.

### Built-in safeguards

1. **Vector-score gate** — returns empty when BM25 finds nothing and all vector scores are below 0.55
2. **Reranker gate** — returns empty when BM25 finds nothing and the best reranker score is below 0.1
3. **Score cap** — clamps blended scores to the best raw vector similarity when BM25 is absent
4. **Confidence gap filter** — drops results scoring below 50% of the top result
5. **Raised defaults** — `vsearch --min-score 0.5`, `query --min-score 0.2` (overridable)

For details on how these work and the underlying research, see [How QMD Search Works](docs/hybrid-search-guide.md).

## Benchmarking

QMD includes tools to measure and calibrate search quality:

- **`qmd profile-embeddings`** — profiles the embedding model's similarity distribution on your indexed corpus, helping you set an optimal `--min-score` threshold
- **`qmd bench <fixture.json>`** — runs queries through all search backends (bm25, vector, hybrid, full) and measures precision, recall, MRR, F1, and latency against expected results

See [How QMD Search Works](docs/hybrid-search-guide.md) for full instructions on creating benchmark fixtures and interpreting results.

## Divergence from upstream

Future changes in this repository may diverge from the original qmd. There are no plans to backport future qmd updates — this port will evolve independently in its own direction.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11
- CUDA 12 toolkit (optional, for GPU acceleration)

## Building

```bash
# Build
dotnet build Qmd.slnx -c Release

# Run tests
dotnet test Qmd.slnx -c Release

# Publish self-contained executable
dotnet publish src/Qmd.Cli/Qmd.Cli.csproj -c Release -r win-x64 --self-contained
```

The published output will be in `src/Qmd.Cli/bin/Release/net8.0/win-x64/publish/`.

## License

MIT — see [LICENSE](LICENSE) for details.

This project includes the original copyright notice from qmd.
