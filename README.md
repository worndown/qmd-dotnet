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

There is no roadmap at this point. The focus is on making it work properly.

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
