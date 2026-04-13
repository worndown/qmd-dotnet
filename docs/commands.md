# Command Reference

Complete reference for all `qmd` commands and subcommands.

**Global option:** `--index <name>` — Named index to use (default: `index`). Applies to every command.

## Contents

- [Search](#search)
  - [search](#qmd-search)
  - [vsearch](#qmd-vsearch)
  - [query](#qmd-query)
- [Retrieval](#retrieval)
  - [get](#qmd-get)
  - [multi-get](#qmd-multi-get)
  - [ls](#qmd-ls)
- [Indexing](#indexing)
  - [update](#qmd-update)
  - [embed](#qmd-embed)
  - [cleanup](#qmd-cleanup)
- [Collections](#collections)
  - [collection list](#qmd-collection-list)
  - [collection add](#qmd-collection-add)
  - [collection remove](#qmd-collection-remove)
  - [collection rename](#qmd-collection-rename)
  - [collection show](#qmd-collection-show)
  - [collection update-cmd](#qmd-collection-update-cmd)
  - [collection include / exclude](#qmd-collection-include--exclude)
- [Contexts](#contexts)
  - [context list](#qmd-context-list)
  - [context add](#qmd-context-add)
  - [context rm](#qmd-context-rm)
  - [context check](#qmd-context-check)
- [Models](#models)
  - [pull](#qmd-pull)
- [MCP Server](#mcp-server)
  - [mcp](#qmd-mcp)
  - [mcp stop](#qmd-mcp-stop)
- [Skills](#skills)
  - [skill show](#qmd-skill-show)
  - [skill install](#qmd-skill-install)
- [Diagnostics](#diagnostics)
  - [status](#qmd-status)
  - [bench](#qmd-bench)
  - [profile-embeddings](#qmd-profile-embeddings)

---

## Search

### `qmd search`

BM25 full-text keyword search using SQLite FTS5. Fast, requires no models. Returns nothing when query terms are absent from the corpus.

```
qmd search <query> [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--limit` | `-n` | `5` | Max results |
| `--collection` | `-c` | — | Filter by collection (repeatable) |
| `--min-score` | — | `0` | Minimum BM25 relevance score |
| `--all` | — | — | Return all results |
| `--format` | — | `cli` | Output format: cli, json, csv, md, xml, files |
| `--json` | — | — | Alias for `--format json` |
| `--csv` | — | — | Alias for `--format csv` |
| `--md` | — | — | Alias for `--format md` |
| `--xml` | — | — | Alias for `--format xml` |
| `--files` | — | — | Alias for `--format files` |
| `--full` | — | — | Show full document content |
| `--line-numbers` | — | — | Prefix output with line numbers |

---

### `qmd vsearch`

Vector cosine similarity search. Alias: `vector-search`. Requires embedding model (`qmd pull`).

```
qmd vsearch <query> [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--limit` | `-n` | `10` | Max results |
| `--collection` | `-c` | — | Filter by collection (repeatable) |
| `--min-score` | — | `0.5` | Minimum cosine similarity (0–1) |
| `--intent` | — | — | Domain context for query expansion |
| `--all` | — | — | Return all results |
| `--format` | — | `cli` | Output format |
| `--json / --csv / --md / --xml / --files` | — | — | Format aliases |
| `--full` | — | — | Show full document content |
| `--line-numbers` | — | — | Prefix output with line numbers |

---

### `qmd query`

Hybrid search: BM25 + vector search merged via RRF, then ranked by LLM reranker. Alias: `deep-search`. Requires embedding and reranker models (`qmd pull`).

```
qmd query <query> [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--limit` | `-n` | `10` | Max results |
| `--collection` | `-c` | — | Filter by collection (repeatable) |
| `--min-score` | — | `0.2` | Minimum relevance score |
| `--intent` | — | — | Domain context for query expansion |
| `--no-rerank` | — | — | Skip LLM reranking (faster, raw RRF scores) |
| `--candidate-limit` | `-C` | `40` | Max candidates passed to reranker |
| `--chunk-strategy` | — | `regex` | Chunking: `regex` or `auto` (AST for code) |
| `--explain` | — | — | Show per-document retrieval traces |
| `--all` | — | — | Return all results |
| `--format` | — | `cli` | Output format |
| `--json / --csv / --md / --xml / --files` | — | — | Format aliases |
| `--full` | — | — | Show full document content |
| `--line-numbers` | — | — | Prefix output with line numbers |

---

## Retrieval

### `qmd get`

Retrieve a single document by file path, virtual path, or docid.

```
qmd get <file> [options]
```

**Arguments:**
- `<file>` — File path, virtual path (`qmd://collection/path`), or docid (`#abc123`). Supports a `:N` suffix as shorthand for `--from N` (e.g., `notes.md:50`).

| Option | Short | Default | Description |
|---|---|---|---|
| `--from` | — | — | Start line (1-indexed) |
| `--lines` | `-l` | — | Max lines to return |
| `--line-numbers` | — | — | Prefix output with line numbers |

**Examples:**
```bash
qmd get docs/architecture.md
qmd get docs/architecture.md:100 --lines 50   # lines 100–149
qmd get "#abc123"                              # retrieve by docid
```

---

### `qmd multi-get`

Retrieve multiple documents by glob pattern or comma-separated list of paths/docids.

```
qmd multi-get <pattern> [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--lines` | `-l` | — | Max lines per file |
| `--max-bytes` | — | `10240` | Skip files larger than this (bytes) |
| `--format` | — | `cli` | Output format |
| `--json / --csv / --md / --xml / --files` | — | — | Format aliases |
| `--line-numbers` | — | — | Prefix output with line numbers |

**Examples:**
```bash
qmd multi-get "docs/**/*.md" --lines 100
qmd multi-get "notes/a.md,notes/b.md" --json
```

---

### `qmd ls`

List collections or files within a collection.

```
qmd ls [<path>]
```

- No argument — lists all registered collections
- `<name>` — lists files in the named collection
- `qmd://name/prefix` — lists files under the given virtual path prefix

**Examples:**
```bash
qmd ls                        # all collections
qmd ls notes                  # files in the "notes" collection
qmd ls qmd://notes/2024/      # files under the 2024/ path
```

---

## Indexing

### `qmd update`

Re-index all registered collections. Detects added, modified, and removed files.

```
qmd update [options]
```

| Option | Description |
|---|---|
| `--pull` | Run `git pull` in each collection directory before re-indexing |

If a collection has a custom `update-cmd` set (see `collection update-cmd`), that command is run instead of `git pull`.

---

### `qmd embed`

Generate vector embeddings for documents that do not have them yet. Requires the embedding model (`qmd pull`).

```
qmd embed [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--force` | `-f` | — | Re-embed all documents, not just new/changed ones |
| `--chunk-strategy` | — | `regex` | Chunking: `regex` or `auto` (AST for code files) |
| `--max-docs-per-batch` | — | `64` | Max documents per batch |
| `--max-batch-mb` | — | `64` | Max MB of content per batch |

---

### `qmd cleanup`

Clear LLM response caches, remove orphaned rows from deleted collections, and vacuum the SQLite database.

```
qmd cleanup
```

No options. Runs all maintenance steps in sequence:
- Clears cached API responses
- Removes documents from deleted collections
- Removes orphaned content and vector rows
- Removes inactive document records
- Runs SQLite VACUUM to reclaim disk space

---

## Collections

A collection is a directory of markdown files registered with qmd. All search commands operate across all included collections by default, or against a specific collection when `--collection` is used.

### `qmd collection list`

List all registered collections with their status, file count, pattern, and last update time.

```
qmd collection list
```

---

### `qmd collection add`

Register a directory as a new collection and immediately index its files.

```
qmd collection add [<path>] [options]
```

**Arguments:**
- `<path>` — Directory to index (default: `.`)

| Option | Default | Description |
|---|---|---|
| `--name` | (directory name) | Collection name; auto-derived from directory basename if omitted |
| `--mask` | `**/*.md` | Glob pattern for files to include |
| `--ignore` | — | Glob patterns to exclude (repeatable) |

**Examples:**
```bash
qmd collection add ~/notes --name notes
qmd collection add ./docs --mask "**/*.{md,rst}" --ignore "**/drafts/**"
```

---

### `qmd collection remove`

Remove a collection and all its indexed documents from the store. Aliases: `rm`.

```
qmd collection remove <name>
qmd collection rm <name>
```

---

### `qmd collection rename`

Rename a collection. Aliases: `mv`.

```
qmd collection rename <old> <new>
qmd collection mv <old> <new>
```

---

### `qmd collection show`

Show full details for a collection: path, pattern, include status, update command, ignore patterns, and context entries. Aliases: `info`.

```
qmd collection show <name>
qmd collection info <name>
```

---

### `qmd collection update-cmd`

Set or clear the custom shell command that runs before `qmd update` for a collection. Aliases: `set-update`.

```
qmd collection update-cmd <name> [<command>]
qmd collection set-update <name> [<command>]
```

Omitting `<command>` clears the current update command.

**Example:**
```bash
# Pull latest from remote before re-indexing
qmd collection update-cmd notes "git -C ~/notes pull --ff-only"

# Clear the update command
qmd collection update-cmd notes
```

---

### `qmd collection include` / `exclude`

Control whether a collection is included in default (unfiltered) searches.

```
qmd collection include <name>   # include in default searches
qmd collection exclude <name>   # exclude from default searches
```

Excluded collections are still searchable with `--collection <name>`.

---

## Contexts

Contexts are short descriptions associated with collection paths. They are passed to LLM components (query expansion, reranking) to help them understand what a collection or directory contains. Adding good context descriptions improves search accuracy.

### `qmd context list`

List all context entries across all collections.

```
qmd context list
```

---

### `qmd context add`

Add a context description to a path.

```
qmd context add [<path>] <text>
```

**Arguments:**
- `<path>` — The path to annotate (default: `.`). Accepts:
  - A filesystem path (auto-resolved to a collection)
  - A virtual path: `qmd://collection/subpath`
  - `/` to set a global context that applies across all collections
- `<text>` — A short description of the content at this path

**Examples:**
```bash
qmd context add qmd://notes/ "Personal engineering notes and decisions"
qmd context add qmd://notes/2024/ "Notes from 2024 — focus on distributed systems"
qmd context add / "Personal knowledge base: software engineering, architecture, and personal notes"
```

---

### `qmd context rm`

Remove a context entry. Aliases: `remove`.

```
qmd context rm <path>
qmd context remove <path>
```

Pass `/` to remove the global context.

---

### `qmd context check`

Report collections and paths that have no context description set.

```
qmd context check
```

---

## Models

### `qmd pull`

Download the three local LLM models used by qmd. Models are stored in the user's local application data directory and persist across index operations.

```
qmd pull [options]
```

| Option | Description |
|---|---|
| `--refresh` | Re-download all models even if already cached |

The three models downloaded are:
- **Embedding model** (embeddinggemma-300M) — used by `vsearch` and `query`
- **Reranker** (Qwen3-Reranker-0.6B) — used by `query`
- **Generator** — used for query expansion in `vsearch` and `query`

Total download size is approximately 1.5 GB. Only needed once.

---

## MCP Server

### `qmd mcp`

Start the MCP (Model Context Protocol) server to expose qmd search tools to AI assistants such as Claude.

```
qmd mcp [options]
```

| Option | Default | Description |
|---|---|---|
| `--http` | — | Use HTTP transport instead of stdio |
| `--port` | `8181` | HTTP port (only with `--http`) |
| `--daemon` | — | Run as a background daemon (only with `--http`) |

Without flags, starts in **stdio mode** — the standard integration for Claude Desktop and agent configurations. In stdio mode, qmd acts as an MCP server that reads from stdin and writes to stdout.

**Examples:**
```bash
qmd mcp                        # stdio mode (Claude Desktop)
qmd mcp --http --port 8181     # HTTP mode
qmd mcp --http --daemon        # background HTTP daemon
```

---

### `qmd mcp stop`

Stop a running MCP daemon (HTTP mode only).

```
qmd mcp stop
```

---

## Skills

### `qmd skill show`

Print the embedded `SKILL.md` to stdout. The skill file describes qmd's capabilities in a format understood by AI assistants.

```
qmd skill show
```

---

### `qmd skill install`

Install the qmd skill files into the current directory (or home directory with `--global`) for use with AI assistant integrations.

```
qmd skill install [options]
```

| Option | Short | Description |
|---|---|---|
| `--global` | — | Install to home directory instead of current directory |
| `--yes` | — | Auto-confirm symlink creation without prompting |
| `--force` | `-f` | Overwrite an existing install |

---

## Diagnostics

### `qmd status`

Show index health, collection status, available models, and MCP daemon state.

```
qmd status
```

Output includes:
- Document count and how many need re-embedding
- Vector index status
- Last update timestamp
- MCP daemon status (running / stopped)
- Paths to available models (embed, rerank, generate)
- AST chunking: supported languages
- Per-collection status table
- Any warnings or recommendations

---

### `qmd bench`

Run a benchmark fixture against all search backends and report quality metrics.

```
qmd bench <fixture.json> [options]
```

| Option | Short | Description |
|---|---|---|
| `--json` | — | Output results as JSON |
| `--collection` | `-c` | Override collection filter for all queries |

The fixture is a JSON file containing queries and their expected result files. Four backends are tested: bm25, vector, hybrid, full. Metrics reported: Precision@k, Recall, MRR, F1, and Latency.

See [Hybrid Search Internals](hybrid-search-guide.md) for fixture format details and guidance on writing effective test queries.

---

### `qmd profile-embeddings`

Profile the embedding model's cosine similarity distribution on your indexed corpus to help calibrate `--min-score` for `vsearch`.

```
qmd profile-embeddings [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--sample-size` | `-n` | `100` | Number of random chunk pairs to sample |
| `--collection` | `-c` | — | Filter by collection (repeatable) |

Outputs a percentile table (min, P5, P25, median, mean, P75, P95, max) and a suggested `--min-score` value. The P75 percentile is generally a good starting point.

**Example:**
```bash
qmd profile-embeddings --sample-size 500 --collection notes
```
