# Custom Models

qmd ships with three built-in models for embeddings, reranking, and query expansion. You can replace any of them with a different GGUF model from Hugging Face using environment variables — no rebuild required.

---

## Environment variables

| Variable | Model role | Default |
|---|---|---|
| `QMD_EMBED_MODEL` | Generates vector embeddings for `vsearch` and `query` | `hf:worndown/Qwen3-Embedding-0.6B-GGUF/Qwen3-Embedding-0.6B-f16.gguf` |
| `QMD_RERANK_MODEL` | Scores relevance of candidates in `query` | `hf:worndown/Qwen3-Reranker-0.6B-GGUF/Qwen3-Reranker-0.6B-f16.gguf` |
| `QMD_GENERATE_MODEL` | Expands queries with alternative phrasings | `hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-f16.gguf` |
| `QMD_EXPAND_CONTEXT_SIZE` | Token context window for the generate model | `2048` |

Set one or more before running qmd:

```powershell
# PowerShell
$env:QMD_EMBED_MODEL = "hf:nomic-ai/nomic-embed-text-v1.5-GGUF/nomic-embed-text-v1.5.f16.gguf"
qmd embed
```

```cmd
:: Command Prompt
set QMD_EMBED_MODEL=hf:nomic-ai/nomic-embed-text-v1.5-GGUF/nomic-embed-text-v1.5.f16.gguf
qmd embed
```

To make a model persistent, set the variable in your user or system environment settings so it applies to every qmd session.

---

## The `hf:` URI format

Models are identified by a URI of the form:

```
hf:<user>/<repo>/<file.gguf>
hf:<user>/<repo>/<subdir>/<file.gguf>
```

The first two path segments identify the Hugging Face repository (`user/repo`). Everything after that is the file path within the repository, which may include subdirectories.

### Converting a Hugging Face URL

1. Go to the model page on Hugging Face, e.g. `https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF`
2. Click the **Files and versions** tab
3. Find the `.gguf` file you want and copy its path as shown in the file list

The download URL shown in the browser looks like:

```
https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q8_0.gguf
```

Strip the protocol and `resolve/main/`, then prefix with `hf:`:

```
                  ┌── user ──┐ ┌──────────── repo ────────────┐ ┌──────────── file ────────────┐
URL path:         bartowski / Llama-3.2-1B-Instruct-GGUF / resolve/main / Llama-3.2-1B-Instruct-Q8_0.gguf
hf: URI:   hf:   bartowski / Llama-3.2-1B-Instruct-GGUF /              Llama-3.2-1B-Instruct-Q8_0.gguf
```

Result:
```
hf:bartowski/Llama-3.2-1B-Instruct-GGUF/Llama-3.2-1B-Instruct-Q8_0.gguf
```

For a file in a subdirectory, keep the subdirectory as part of the file path:

```
URL:    https://huggingface.co/user/repo/resolve/main/subdir/model-q4.gguf
hf: URI: hf:user/repo/subdir/model-q4.gguf
```

### Using a local file

If you already have a GGUF file on disk, pass its absolute path instead of an `hf:` URI:

```powershell
$env:QMD_EMBED_MODEL = "C:\models\my-embedding-model.gguf"
```

---

## Model cache

Downloaded models are cached in `%LOCALAPPDATA%\qmd\models\`. On subsequent runs qmd checks the ETag from Hugging Face and skips the download if the file is current. To force a re-download, run `qmd pull --refresh`.

---

## Caveats

### Re-embedding after changing the embed model

Embeddings are tied to the model that produced them. If you switch `QMD_EMBED_MODEL`, run `qmd embed --force` to regenerate all vectors with the new model before searching. Searching with mismatched embeddings will return poor or nonsensical results.

### Prompt formatting and embedding models

qmd automatically selects a prompt format based on the model URI. If the URI contains `qwen` and `embed` (case-insensitive), it uses Qwen3-Embedding's instruct format. All other models receive a generic query/document format. If you switch to a model with its own required prompt template that differs from both of these, results may degrade — check the model card for guidance.

### Context size for the generate model

`QMD_EXPAND_CONTEXT_SIZE` controls the token budget for query expansion. Increase it if the generate model you use produces truncated output with the default 2048 tokens. Setting it higher than the model's maximum trained context has no effect beyond wasting memory.
