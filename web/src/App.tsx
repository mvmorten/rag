import { useRef, useState, type FormEvent, type ReactNode } from 'react'
import { ask, SEARCH_MODES, type AnswerResult, type SearchMode } from './api'

const REFUSAL_LABELS: Record<AnswerResult['refusal'], string> = {
  None: '',
  NoSources: 'Nothing was retrieved for this question.',
  ModelDeclined: 'The model judged the retrieved sources insufficient to answer.',
  NoValidCitation: 'The model produced an answer without a valid citation, so it was discarded.',
  InvalidOutput: 'The model returned output that could not be parsed.',
}

const EXAMPLES = [
  'How do I scale based on the length of an Azure Service Bus queue?',
  'How can I pull an image from Azure Container Registry using a managed identity?',
  'How do I split incoming traffic between two revisions?',
  'How much does an Azure SQL Managed Instance cost per month?',
]

/** Turns [n] markers in the answer into anchors that jump to the matching source. */
function renderAnswer(text: string, count: number): ReactNode[] {
  return text.split(/(\[\d+\]|`[^`]+`)/g).map((part, i) => {
    if (part.length > 2 && part.startsWith('`') && part.endsWith('`')) {
      return <code key={i}>{part.slice(1, -1)}</code>
    }
    const m = /^\[(\d+)\]$/.exec(part)
    if (!m) return part
    const n = Number(m[1])
    if (n < 1 || n > count) return part
    return (
      <a key={i} className="cite" href={`#source-${n}`}>
        {n}
      </a>
    )
  })
}

export default function App() {
  const [question, setQuestion] = useState('')
  const [mode, setMode] = useState<SearchMode>('Hybrid')
  const [result, setResult] = useState<AnswerResult | null>(null)
  const [asked, setAsked] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const controller = useRef<AbortController | null>(null)

  async function submit(q: string) {
    const trimmed = q.trim()
    if (!trimmed) return
    controller.current?.abort()
    controller.current = new AbortController()
    setLoading(true)
    setError(null)
    try {
      const r = await ask(trimmed, mode, controller.current.signal)
      setResult(r)
      setAsked(trimmed)
    } catch (e) {
      if ((e as Error).name === 'AbortError') return
      setResult(null)
      setError((e as Error).message)
    } finally {
      setLoading(false)
    }
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    void submit(question)
  }

  const cited = new Set(result?.citations.map((c) => c.index))

  return (
    <main>
      <header>
        <h1>Ask the Container Apps docs</h1>
        <p className="sub">
          Grounded answers over Azure Container Apps documentation, with citations and the exact chunks retrieved.
        </p>
      </header>

      <form onSubmit={onSubmit}>
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault()
              void submit(question)
            }
          }}
          placeholder="Ask a question about Azure Container Apps…"
          rows={2}
          maxLength={1000}
          aria-label="Question"
        />
        <div className="controls">
          <label>
            Retrieval
            <select value={mode} onChange={(e) => setMode(e.target.value as SearchMode)}>
              {SEARCH_MODES.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" disabled={loading || !question.trim()}>
            {loading ? 'Thinking…' : 'Ask'}
          </button>
        </div>
      </form>

      {!result && !loading && !error && (
        <section className="examples">
          <h2>Try one</h2>
          <ul>
            {EXAMPLES.map((ex) => (
              <li key={ex}>
                <button
                  type="button"
                  className="link"
                  onClick={() => {
                    setQuestion(ex)
                    void submit(ex)
                  }}
                >
                  {ex}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {result && (
        <>
          <section className={`answer ${result.answerable ? '' : 'refused'}`} aria-live="polite">
            <p className="asked">{asked}</p>
            {result.answerable ? (
              <p className="text">{renderAnswer(result.answer, result.retrieved.length)}</p>
            ) : (
              <>
                <p className="text">{result.answer}</p>
                <p className="reason">{REFUSAL_LABELS[result.refusal]}</p>
              </>
            )}
          </section>

          {result.citations.length > 0 && (
            <section>
              <h2>Cited sources</h2>
              <ol className="citations">
                {result.citations.map((c) => (
                  <li key={c.index} value={c.index}>
                    <a href={c.url} target="_blank" rel="noreferrer">
                      {c.sectionPath}
                    </a>
                  </li>
                ))}
              </ol>
            </section>
          )}

          <section>
            <h2>
              Retrieved chunks <span className="count">({result.retrieved.length})</span>
            </h2>
            <p className="hint">
              Everything the model saw. Highlighted chunks were cited; the rest were retrieved but not used.
            </p>
            <ol className="chunks">
              {result.retrieved.map((c, i) => (
                <li key={c.id} id={`source-${i + 1}`} className={cited.has(i + 1) ? 'used' : ''}>
                  <details>
                    <summary>
                      <span className="n">{i + 1}</span>
                      <span className="path">{c.sectionPath}</span>
                      <span className="score" title="Search score (RRF for hybrid modes)">
                        {c.score.toFixed(3)}
                        {c.rerankerScore != null && ` · rerank ${c.rerankerScore.toFixed(2)}`}
                      </span>
                    </summary>
                    <pre>{c.content}</pre>
                    <a href={c.url} target="_blank" rel="noreferrer">
                      Open on Microsoft Learn ↗
                    </a>
                  </details>
                </li>
              ))}
            </ol>
          </section>
        </>
      )}
    </main>
  )
}
