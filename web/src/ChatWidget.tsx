import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, ApiError, type ChatMessage } from './api'
import { useI18n } from './i18n'

const SESSION_KEY = 'chatSession'

/** Stable random id per browser, so the conversation survives page reloads. */
function sessionId(): string {
  try {
    const existing = localStorage.getItem(SESSION_KEY)
    if (existing) return existing
    const id = crypto.randomUUID()
    localStorage.setItem(SESSION_KEY, id)
    return id
  } catch {
    return crypto.randomUUID()
  }
}

export default function ChatWidget() {
  const { t, lang } = useI18n()
  const [open, setOpen] = useState(false)
  const [session] = useState(sessionId)
  const [messages, setMessages] = useState<ChatMessage[] | null>(null)
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const list = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLTextAreaElement>(null)

  // Load earlier messages the first time the chat opens.
  useEffect(() => {
    if (!open || messages) return
    api
      .chatHistory(session)
      .then(setMessages)
      .catch(() => setMessages([]))
  }, [open, messages, session])

  useEffect(() => {
    list.current?.scrollTo({ top: list.current.scrollHeight, behavior: 'smooth' })
  }, [messages, sending])

  useEffect(() => {
    if (open) input.current?.focus()
  }, [open])

  const send = async (e?: FormEvent) => {
    e?.preventDefault()
    const text = draft.trim()
    if (!text || sending) return
    setDraft('')
    setNotice(null)
    setMessages((m) => [...(m ?? []), { role: 'user', text, at: new Date().toISOString() }])
    setSending(true)
    try {
      const res = await api.chat({ sessionId: session, message: text, language: lang })
      setMessages((m) => [...(m ?? []), { role: 'assistant', text: res.reply, at: new Date().toISOString() }])
    } catch (err) {
      const status = err instanceof ApiError ? err.status : 0
      setNotice(status === 429 ? t('chat.tooMany') : status === 503 ? t('chat.unavailable') : t('chat.error'))
      setDraft(text)
      setMessages((m) => (m ?? []).slice(0, -1))
    } finally {
      setSending(false)
    }
  }

  return (
    <>
      {open && (
        <section className="chat" aria-label={t('chat.title')}>
          <header className="chat-head">
            <div>
              <strong>{t('chat.title')}</strong>
              <span className="muted small">{t('chat.subtitle')}</span>
            </div>
            <button className="chat-close" onClick={() => setOpen(false)} aria-label={t('chat.close')}>
              ×
            </button>
          </header>
          <div className="chat-messages" ref={list}>
            <p className="bubble assistant">{t('chat.greeting')}</p>
            {messages?.map((m, i) => (
              <p key={i} className={`bubble ${m.role}`}>
                {m.text}
              </p>
            ))}
            {sending && (
              <p className="bubble assistant typing" aria-label={t('chat.typing')}>
                <span />
                <span />
                <span />
              </p>
            )}
          </div>
          {notice && <p className="chat-notice">{notice}</p>}
          <form className="chat-input" onSubmit={send}>
            <textarea
              ref={input}
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  void send()
                }
              }}
              placeholder={t('chat.placeholder')}
              rows={1}
              maxLength={1500}
            />
            <button className="btn btn-small" disabled={sending || !draft.trim()}>
              {t('chat.send')}
            </button>
          </form>
        </section>
      )}
      <button className={`chat-fab ${open ? 'hidden' : ''}`} onClick={() => setOpen(true)} aria-label={t('chat.open')}>
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden>
          <path
            fill="currentColor"
            d="M4 4h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9l-4.5 3.6A.9.9 0 0 1 3 20.9V6a2 2 0 0 1 1-2Z"
          />
        </svg>
        <span>{t('chat.open')}</span>
      </button>
    </>
  )
}
