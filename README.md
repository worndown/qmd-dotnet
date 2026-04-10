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

## Divergence from upstream

Future changes in this repository may diverge from the original qmd. There are no plans to backport future qmd updates — this port will evolve independently in its own direction.

### RRF Limitations & Mitigations

Standard RRF is purely positional — it discards score magnitude and treats a 0.99 similarity match identically to a 0.50 match if they share the same rank ([Bruch et al., ACM TOIS 2023](https://arxiv.org/abs/2210.11934)).

When BM25 returns zero results (the query has no keyword matches in the corpus), vector-only results receive inflated RRF scores with no cross-system agreement to validate them, and the reranker at only 25% weight cannot veto an irrelevant top-ranked result.

The **qmd-dotnet** adds four safeguards that do not exist in the original **qmd** version: 
1) a **vector-score gate** that returns empty results when BM25 finds nothing and all vector similarities are below 0.55 (the noise floor for most embedding models);
2) a **reranker gate** that returns empty when the best Qwen3-Reranker score is below 0.1, indicating the reranker considers everything irrelevant;
3) a **score cap** that clamps blended scores to the best raw vector similarity when BM25 is absent, preventing positional RRF scores from exceeding the actual semantic signal; and
4) a **confidence gap filter** that drops results scoring below 50% of the top result. The CLI defaults are also raised: `vsearch --min-score` defaults to 0.5 (was 0.3) and `query --min-score` defaults to 0.2 (was 0.0). Use `qmd profile-embeddings` to measure the actual similarity distribution on your corpus and calibrate thresholds for your embedding model.

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
