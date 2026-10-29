# Infrastructure

[main.bicep](main.bicep) defines the full stack: managed identity, Azure OpenAI (+ 3 model deployments), AI Search, Container Apps environment and app, ACR, Log Analytics, and role assignments. Auth is Entra-only, so there is deliberately no Key Vault (nothing to store). The [deploy workflow](../.github/workflows/deploy.yml) provisions it, builds the image in ACR, and rolls the app. `az deployment group what-if` passes against this template.

## Deployed environment

Deployed once from a developer login with `az deployment group create` (the Bicep above), resource group `rg-rag-prod` in eastus2 (Search in centralus, where Basic had capacity). The earlier hand-made `rg-rag-dev` was deleted once this reproduced its eval numbers exactly.

| Resource | Name |
|---|---|
| Container App (API + UI) | `ca-ragprod-vqcwmpwlk3sw6` |
| Azure OpenAI | `aoai-ragprod-vqcwmpwlk3sw6` |
| Azure AI Search (Basic) | `srch-ragprod-vqcwmpwlk3sw6` |
| Container Registry | `acrragprodvqcwmpwlk3sw6` |

Model deployments: `text-embedding-3-small` (embeddings), `gpt-5.4-mini` (answers), `gpt-5.4` (eval judge).

The container app is public, scales to zero, and is capped at 2 replicas with a per-IP rate limit, because each request spends model tokens. Ingestion is not part of the app: run `Rag.Ingest` with a developer identity (the template can grant one via `developerPrincipalId`).

**Cost:** Search Basic bills hourly (~$0.10/hr, ~$75/month) while it exists. Tear down with `az group delete -n rg-rag-prod`.

## CI identity

Entra app registration `rag-ci-github` with federated credentials for this repo's `main` branch and pull requests. Its subjects use GitHub's immutable form (`repo:mvmorten@<owner-id>/rag@<repo-id>:...`) because the repo has `use_immutable_subject` enabled; a name-based subject does not match. Roles: Cognitive Services OpenAI User on the OpenAI account, Search Index Data Reader on the search service. Client, tenant and subscription IDs are stored as repo variables (not secrets).
