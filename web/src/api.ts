export type SearchMode = 'Keyword' | 'Vector' | 'Hybrid' | 'HybridSemantic'

export const SEARCH_MODES: { value: SearchMode; label: string }[] = [
  { value: 'Hybrid', label: 'Hybrid (keyword + vector)' },
  { value: 'HybridSemantic', label: 'Hybrid + semantic ranker' },
  { value: 'Vector', label: 'Vector only' },
  { value: 'Keyword', label: 'Keyword only' },
]

export interface Citation {
  index: number
  url: string
  title: string
  sectionPath: string
}

export interface RetrievedChunk {
  id: string
  url: string
  title: string
  sectionPath: string
  content: string
  score: number
  rerankerScore: number | null
}

export interface AnswerResult {
  answerable: boolean
  answer: string
  citations: Citation[]
  retrieved: RetrievedChunk[]
  refusal: 'None' | 'NoSources' | 'ModelDeclined' | 'NoValidCitation' | 'InvalidOutput'
}

export async function ask(question: string, mode: SearchMode, signal?: AbortSignal): Promise<AnswerResult> {
  const res = await fetch('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, mode }),
    signal,
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new Error(body?.error ?? `Request failed (${res.status})`)
  }
  return res.json()
}
