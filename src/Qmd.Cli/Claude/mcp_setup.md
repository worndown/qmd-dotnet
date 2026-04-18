# QMD MCP Server Setup (Windows)

This guide targets Windows 10/11 and the Command Prompt (`cmd.exe`). All paths and commands assume that shell.

## Download

Fetch and extract the latest release with in-box tools (`curl.exe` and `tar.exe` both ship with Windows 10+):

```cmd
curl -L -o "%TEMP%\qmd-win-x64.zip" https://github.com/worndown/qmd-dotnet/releases/latest/download/qmd-win-x64.zip
mkdir "%LOCALAPPDATA%\qmd"
tar -xf "%TEMP%\qmd-win-x64.zip" -C "%LOCALAPPDATA%\qmd"
del "%TEMP%\qmd-win-x64.zip"
```

Add the install folder to your user `PATH` (takes effect in new shells):

```cmd
setx PATH "%PATH%;%LOCALAPPDATA%\qmd"
```

Open a new Command Prompt and verify:

```cmd
qmd --version
```

## Install

Pull the local LLM models (~1.5 GB, cached after first run), create a collection from your markdown, and build embeddings:

```cmd
qmd pull
qmd collection add "C:\Users\%USERNAME%\notes" --name myknowledge
qmd embed
```

## Configure MCP Client

**Claude Code** (`%USERPROFILE%\.claude\settings.json`):
```json
{
  "mcpServers": {
    "qmd": { "command": "qmd", "args": ["mcp"] }
  }
}
```

**Claude Desktop** (`%APPDATA%\Claude\claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "qmd": { "command": "qmd", "args": ["mcp"] }
  }
}
```

## HTTP Mode

```cmd
qmd mcp --http              :: Port 8181
qmd mcp --http --daemon     :: Background
qmd mcp stop                :: Stop daemon
```

## Tools

### query

Search with a plain string or typed sub-queries. Pass `searches` as a JSON-encoded string.

```json
{
  "query": "natural language question",
  "searches": "[{\"type\":\"lex\",\"query\":\"keyword phrases\"},{\"type\":\"vec\",\"query\":\"question\"},{\"type\":\"hyde\",\"query\":\"hypothetical answer passage...\"}]",
  "limit": 10,
  "minScore": 0.0,
  "collection": "docs,notes",
  "intent": "optional domain context",
  "candidateLimit": 40,
  "rerank": true
}
```

Use either `query` (plain text) or `searches` (typed sub-queries) — not both. `collection` is a single comma-separated string, not an array. The first sub-query gets 2x weight in ranking.

| Type   | Method | Input                                      |
|--------|--------|--------------------------------------------|
| `lex`  | BM25   | Keywords (2-5 terms)                       |
| `vec`  | Vector | Natural language question                  |
| `hyde` | Vector | Hypothetical answer passage (50-100 words) |

### get

Retrieve a single document by file path, virtual path (`qmd://...`), or `#docid`.

| Param         | Type    | Description                                     |
|---------------|---------|-------------------------------------------------|
| `file`        | string  | Path or `#docid`. Append `:N` for line offset.  |
| `fromLine`    | number? | Start from this line (1-indexed)                |
| `maxLines`    | number? | Maximum lines to return                         |
| `lineNumbers` | bool?   | Prefix each line with its number                |

### multi_get

Retrieve multiple documents by glob or comma-separated list.

| Param         | Type    | Description                                     |
|---------------|---------|-------------------------------------------------|
| `pattern`     | string  | Glob (e.g. `docs/*.md`) or comma-separated list |
| `maxLines`    | number? | Maximum lines per file                          |
| `maxBytes`    | number? | Skip files larger than this (default 10240)     |
| `lineNumbers` | bool?   | Prefix each line with its number                |

### status

Index health and collections. No parameters.

## Uninstall

1. Stop the daemon (if running):
   ```cmd
   qmd mcp stop
   ```
2. Remove the `qmd` entry from `mcpServers` in `%USERPROFILE%\.claude\settings.json` and/or `%APPDATA%\Claude\claude_desktop_config.json`.
3. Remove any installed skill and Claude symlink:
   ```cmd
   rmdir /S /Q "%USERPROFILE%\.claude\skills\qmd"
   rmdir /S /Q "%USERPROFILE%\.agents\skills\qmd"
   ```
4. Optionally remove collections and the binary:
   ```cmd
   qmd collection list
   qmd collection remove <name>
   rmdir /S /Q "%LOCALAPPDATA%\qmd"
   ```
   Then remove `%LOCALAPPDATA%\qmd` from your user `PATH` (System Properties > Environment Variables).

## Troubleshooting

- **Not starting**: `where qmd`, then run `qmd mcp` manually to see errors.
- **No results**: `qmd collection list`, then `qmd embed` if any collection needs embedding.
- **Slow first search**: Normal — models load on demand (~1.5 GB total, cached after first run).
