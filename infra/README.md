# Infrastructure

[main.bicep](main.bicep) defines the full stack: managed identity, Azure OpenAI (+ 3 model deployments), AI Search, Container Apps environment and app, ACR, Log Analytics, and role assignments. Auth is Entra-only, so there is deliberately no Key Vault (nothing to store). The [deploy workflow](../.github/workflows/deploy.yml) provisions it, builds the image in ACR, and rolls the app. `az deployment group what-if` passes against this template.

## Dev resources (manually provisioned)

Created with `az` before the Bicep existed; `rg-rag-dev` is what the eval and CI currently use.
Auth is Entra ID (`DefaultAzureCredential`); no keys are used or stored.

| Resource | Name | Region |
|---|---|---|
| Resource group | `rg-rag-dev` | eastus2 |
| Azure OpenAI | `ragdev-aoai-mvmorten` | eastus2 |
| Azure AI Search (Basic) | `ragdev-search-mvmorten` | centralus (eastus2 had no capacity) |

Endpoints:
- OpenAI: `https://ragdev-aoai-mvmorten.openai.azure.com/`
- Search: `https://ragdev-search-mvmorten.search.windows.net`

Model deployments:

| Deployment | Use |
|---|---|
| `text-embedding-3-small` | embeddings |
| `gpt-5.4-mini` | answer generation |
| `gpt-5.4` | eval judge |
| `gpt-4o` | unused (deprecating); safe to delete |

Roles for the developer user: Cognitive Services OpenAI User, Search Index Data Contributor, Search Service Contributor.

**Cost:** Search Basic bills hourly (~$75/month) while it exists. Tear down with `az group delete -n rg-rag-dev`.

## CI identity

Entra app registration `rag-ci-github` with federated credentials for this repo's `main` branch and pull requests. Its subjects use GitHub's immutable form (`repo:mvmorten@<owner-id>/rag@<repo-id>:...`) because the repo has `use_immutable_subject` enabled; a name-based subject does not match. Roles: Cognitive Services OpenAI User on the OpenAI account, Search Index Data Reader on the search service. Client, tenant and subscription IDs are stored as repo variables (not secrets).
