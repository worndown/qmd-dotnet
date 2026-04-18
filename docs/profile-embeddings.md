# Calibrating Search Thresholds with `profile-embeddings`

The `qmd profile-embeddings` command measures the cosine similarity distribution on your indexed corpus. Use it to find an optimal `--min-score` for `vsearch` and to understand how well the default embedding model separates relevant from irrelevant documents in your collection.

## Why calibration matters

Embedding models produce a **noise floor**: unrelated documents in the same language share a baseline cosine similarity well above zero — a phenomenon called anisotropy. For the default embedding model (Qwen3-Embedding-0.6B), this floor is approximately 0.32. A `--min-score` threshold that works well for one corpus or model may not work for another, so qmd provides this command to measure the actual distribution on your data.

> **Tip:** If you want to automatically apply profiling results to qmd's internal search thresholds, use `qmd autotune` instead. It runs `profile-embeddings` internally and derives optimal thresholds from the results. The `profile-embeddings` command is useful when you want to inspect the raw distribution manually.

## Running the command

```bash
qmd profile-embeddings [--sample-size N] [--collection C]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--sample-size` | `-n` | `100` | Number of random chunk pairs to sample |
| `--collection` | `-c` | — | Restrict to one or more collections (repeatable) |

Larger sample sizes give more stable statistics but take longer to compute, since each sample requires an embedding inference call.

## Example output

```
Model: embeddinggemma (768 dimensions)
Corpus: 1200 chunks, sampled 100 (990 score pairs)

Cosine similarity distribution (inter-document):
  Min:    0.214
  P5:     0.298    P25:    0.365
  Median: 0.412    Mean:   0.415
  P75:    0.462    P95:    0.541
  Max:    0.687

Suggested --min-score for vsearch: 0.46 (P75)
```

## How to read the output

| Metric | Meaning |
|---|---|
| **Min / Max** | The full range of cosine similarities observed between random document pairs |
| **P5 / P25** | 5% / 25% of random pairs score below this — very low thresholds that include a lot of noise |
| **Median / Mean** | The center of the noise distribution — most unrelated document pairs score around here |
| **P75** | 75% of random pairs score below this. Scores above P75 are more likely to be genuine matches than background noise. **Recommended starting point for `vsearch --min-score`** |
| **P95** | Only 5% of random pairs exceed this — an aggressive threshold that may filter some legitimate results |

## Applying the result

Use the suggested `--min-score` with `vsearch`:

```bash
qmd vsearch "your query" --min-score 0.46
```

To make the threshold permanent for a session, set it each time you call `vsearch`, or wrap the command in a shell alias.

Note that `query` uses a different blended score (RRF + reranker) and its default of `--min-score 0.2` operates on a different scale — the `profile-embeddings` output does not directly apply to `query`.

## When to recalibrate

- After adding a large new collection with different content or vocabulary
- If you switch to a different embedding model (via `qmd pull`)
- If `vsearch` is returning too many irrelevant results (raise the threshold) or too few results (lower it)

## Automatic calibration with `autotune`

For automatic threshold calibration, use `qmd autotune`:

```bash
qmd autotune                              # profile-based: fast, derives VecOnlyGateThreshold from P25
qmd autotune --fixture evals/fixture.json # bench-based: grid-searches FtsMinSignal + ConfidenceGapRatio
qmd autotune --dry-run                    # preview without saving
qmd autotune --reset                      # revert to defaults
```

Autotune saves calibrated thresholds to the database. They are loaded automatically on every subsequent search. Use `qmd status` to see which thresholds are active.

See the [Command Reference](commands.md#qmd-autotune) for full option details.
