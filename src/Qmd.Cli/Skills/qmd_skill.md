---
name: qmd
description: Search markdown knowledge bases, notes, and documentation using QMD. Use when users ask to search notes, find documents, or look up information.
license: MIT
compatibility: Requires qmd CLI or MCP server. https://github.com/worndown/qmd-dotnet
allowed-tools: Bash(qmd:*), mcp__qmd__*
---

# QMD - Quick Markdown Search

Local search engine for markdown content. Windows-only binary; see references/mcp-setup.md for install and MCP client setup.

## Status

!`qmd status 2>nul || echo Not installed - see https://github.com/worndown/qmd-dotnet/releases`

## MCP: `query`

```json
{
  "searches": "[{\"type\":\"lex\",\"query\":\"CAP theorem consistency\"},{\"type\":\"vec\",\"query\":\"tradeoff between consistency and availability\"}]",
  "collection": "docs",
  "limit": 10
}
```

`searches` is a JSON-encoded string (not a nested array). `collection` is a single comma-separated string. For structured multi-type queries, prefer the MCP `query` tool over the CLI.

### Query Types

| Type | Method | Input |
|------|--------|-------|
| `lex` | BM25 | Keywords - exact terms, names, code |
| `vec` | Vector | Question - natural language |
| `hyde` | Vector | Answer - hypothetical result (50-100 words) |

### Writing Good Queries

**lex (keyword)**
- 2-5 terms, no filler words
- Exact phrase: `"connection pool"` (quoted)
- Exclude terms: `performance -sports` (minus prefix)
- Code identifiers work: `handleError async`

**vec (semantic)**
- Full natural language question
- Be specific: `"how does the rate limiter handle burst traffic"`
- Include context: `"in the payment service, how are refunds processed"`

**hyde (hypothetical document)**
- Write 50-100 words of what the *answer* looks like
- Use the vocabulary you expect in the result

**expand (auto-expand)**
- Use a single-line query (implicit) or `expand: question` on its own line
- Lets the local LLM generate lex/vec/hyde variations
- Do not mix `expand:` with other typed lines - it's either a standalone expand query or a full query document

### Intent (Disambiguation)

When a query term is ambiguous, add `intent` to steer results:

```json
{
  "searches": "[{\"type\":\"lex\",\"query\":\"performance\"}]",
  "intent": "web page load times and Core Web Vitals"
}
```

Intent affects expansion, reranking, chunk selection, and snippet extraction. It does not search on its own - it's a steering signal that disambiguates queries like "performance" (web-perf vs team health vs fitness).

### Combining Types

| Goal | Approach |
|------|----------|
| Know exact terms | `lex` only |
| Don't know vocabulary | Use a single-line query (implicit `expand:`) or `vec` |
| Best recall | `lex` + `vec` |
| Complex topic | `lex` + `vec` + `hyde` |
| Ambiguous query | Add `intent` to any combination above |

First query gets 2x weight in fusion - put your best guess first.

### Lex Query Syntax

| Syntax | Meaning | Example |
|--------|---------|---------|
| `term` | Prefix match | `perf` matches "performance" |
| `"phrase"` | Exact phrase | `"rate limiter"` |
| `-term` | Exclude | `performance -sports` |

Note: `-term` only works in lex queries, not vec/hyde.

### Collection Filtering

```json
{ "collection": "docs" }              // Single
{ "collection": "docs,notes" }        // Multiple (comma-separated, OR)
```

Omit to search all collections.

## Other MCP Tools

| Tool | Use |
|------|-----|
| `get` | Retrieve doc by `file` path or `#docid` |
| `multi_get` | Retrieve multiple by glob/comma-list `pattern` |
| `status` | Collections and health |

## CLI (Windows cmd)

```cmd
qmd query "question"                        :: Auto-expand + rerank
qmd query --json --explain "q"              :: Show score traces (RRF + rerank blend)
qmd search "keywords"                       :: BM25 only (no LLM)
qmd get "#abc123"                           :: By docid
qmd multi-get "journals\2026-*.md" -l 40    :: Batch pull snippets by glob
qmd multi-get notes\foo.md,notes\bar.md     :: Comma-separated list, preserves order
```

For typed sub-queries (`lex` + `vec` + `hyde`), use the MCP `query` tool.

## HTTP API

```cmd
curl -X POST http://localhost:8181/query ^
  -H "Content-Type: application/json" ^
  -d "{\"searches\":[{\"type\":\"lex\",\"query\":\"test\"}]}"
```

## Setup

```cmd
qmd pull
qmd collection add "C:\Users\%USERNAME%\vault" --name vault
qmd embed
```
