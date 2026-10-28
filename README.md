# rag

A grounded Q&A and search service over public Microsoft Learn documentation, built on Azure.

**Status:** early scaffolding. Nothing here is deployed or measured yet.

## Planned scope

1. **Ingestion** (C#): heading-aware chunking, Azure OpenAI embeddings, Azure AI Search index; re-index only changed files.
2. **Retrieval**: keyword vs. vector vs. hybrid (+ semantic ranker), compared on the same questions.
3. **Answer API** (ASP.NET Core): grounded answers with citations; refuses when sources don't support an answer.
4. **UI** (React): answer, cited sources, and retrieved chunks with scores.
5. **Evaluation harness**: hand-written question/expected-source pairs; retrieval hit rate, groundedness, citation accuracy; runs in CI.
6. **MCP server**: search and answer exposed as MCP tools.
7. **Infrastructure as code** (Bicep): AI Search, Azure OpenAI, Container Apps, Key Vault, managed identity.

## Results

_To be filled in with measured eval numbers._
