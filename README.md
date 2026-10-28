# rag

A grounded Q&A and search service over public Microsoft Learn documentation, built on Azure.

**Status:** ingestion, retrieval, the retrieval eval and the answer API are working against a live Azure AI Search index. The answer API works locally (`POST /ask`, `GET /search`). The UI, groundedness eval, MCP server and Bicep are not built yet.

## Planned scope

1. **Ingestion** (C#): heading-aware chunking, Azure OpenAI embeddings, Azure AI Search index; re-index only changed files.
2. **Retrieval**: keyword vs. vector vs. hybrid (+ semantic ranker), compared on the same questions.
3. **Answer API** (ASP.NET Core): grounded answers with citations; refuses when sources don't support an answer.
4. **UI** (React): answer, cited sources, and retrieved chunks with scores.
5. **Evaluation harness**: hand-written question/expected-source pairs; retrieval hit rate, groundedness, citation accuracy; runs in CI.
6. **MCP server**: search and answer exposed as MCP tools.
7. **Infrastructure as code** (Bicep): AI Search, Azure OpenAI, Container Apps, Key Vault, managed identity.

## Results

### Retrieval (40 hand-written questions over 236 Container Apps docs, 3,641 chunks)

Chunk-level: a hit means one of the top-k retrieved chunks comes from a doc labelled as containing the answer.

| Mode | hit@1 | hit@3 | hit@5 | MRR |
|---|---|---|---|---|
| Keyword (BM25) | 65% | 78% | 88% | 0.736 |
| Vector | 70% | 93% | 93% | 0.815 |
| Hybrid (keyword + vector, RRF) | 68% | 93% | 97% | 0.817 |
| Hybrid + semantic ranker | 70% | 88% | 93% | 0.804 |

Caveats, stated plainly:
- n = 40, so one question is 2.5 points. Differences of a few points are noise. What is clear: vector and hybrid beat keyword-only on hit@3 (93% vs 78%), and hybrid gave the best hit@5.
- The semantic ranker did **not** help on this set (it lowered hit@3 and hit@5). I have not tuned it.
- Labels were checked against the corpus (`dotnet run --project src/Rag.Eval -- validate`). One label (q27) was widened after seeing results because a retrieved doc was a legitimate answer; the metrics above are from after that change.
- Questions are written by the project author, not sampled from real user queries.

Reproduce: `dotnet run --project src/Rag.Ingest`, then `dotnet run --project src/Rag.Eval -- run`. Requires the Azure resources in [infra/](infra/README.md).

Corpus: [MicrosoftDocs/azure-docs](https://github.com/MicrosoftDocs/azure-docs) `articles/container-apps/` (CC BY 4.0).
