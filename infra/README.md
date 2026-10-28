# Dev resources (manually provisioned)

Temporary, created with `az` before the Bicep templates exist. Bicep will replace this.
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
| `gpt-4o` | eval judge (may need replacing; 4o is on a deprecation path) |

Roles for the developer user: Cognitive Services OpenAI User, Search Index Data Contributor, Search Service Contributor.

**Cost:** Search Basic bills hourly (~$75/month) while it exists. Tear down with `az group delete -n rg-rag-dev`.
