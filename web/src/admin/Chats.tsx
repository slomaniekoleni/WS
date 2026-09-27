import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { formatDateTime } from '../format'
import { useI18n } from '../i18n'
import { admin, type Conversation, type ConversationSummary } from './adminApi'
import { useAdminT } from './adminI18n'
import { useErrorText } from './AdminApp'

/** Chat list (handoffs first) and a single transcript at /admin/chats/:id. */
export default function Chats({ timeZone }: { timeZone: string }) {
  const { id } = useParams()
  return id ? <Transcript id={Number(id)} timeZone={timeZone} /> : <ChatList timeZone={timeZone} />
}

function ChatList({ timeZone }: { timeZone: string }) {
  const t = useAdminT()
  const { locale } = useI18n()
  const [onlyHandoffs, setOnlyHandoffs] = useState(false)
  const [list, setList] = useState<ConversationSummary[] | null>(null)

  useEffect(() => {
    let cancelled = false
    admin
      .conversations(onlyHandoffs)
      .then((l) => !cancelled && setList(l))
      .catch(() => !cancelled && setList([]))
    return () => {
      cancelled = true
    }
  }, [onlyHandoffs])

  return (
    <div className="admin-page">
      <div className="admin-toolbar">
        <h1>{t('nav.chats')}</h1>
        <div className="chips">
          <button className={`chip ${!onlyHandoffs ? 'active' : ''}`} onClick={() => setOnlyHandoffs(false)}>
            {t('ch.all')}
          </button>
          <button className={`chip ${onlyHandoffs ? 'active' : ''}`} onClick={() => setOnlyHandoffs(true)}>
            {t('ch.needsHuman')}
          </button>
        </div>
      </div>
      {!list && <p className="muted">{t('c.loading')}</p>}
      {list?.length === 0 && <p className="muted">{t('ch.empty')}</p>}
      <div className="admin-list">
        {list?.map((c) => (
          <Link key={c.id} to={`/admin/chats/${c.id}`} className={`card admin-item ${c.needsHuman ? 'attention' : ''}`}>
            <strong>
              {c.needsHuman && '🙋 '}
              {c.clientName ?? `#${c.id}`}
              <span className="muted small"> · {t(`src.${c.channel}`)}</span>
            </strong>
            {c.handoffReason && c.needsHuman && <span className="small">{c.handoffReason}</span>}
            {c.lastMessage && <span className="muted small admin-ellipsis">» {c.lastMessage}</span>}
            <span className="muted small">{formatDateTime(c.lastMessageAtUtc, timeZone, locale)}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}

function Transcript({ id, timeZone }: { id: number; timeZone: string }) {
  const t = useAdminT()
  const { locale } = useI18n()
  const errorText = useErrorText()
  const [conv, setConv] = useState<Conversation | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setConv(await admin.conversation(id))
    } catch (err) {
      setError(errorText(err))
    }
  }, [id, errorText])

  useEffect(() => {
    // Async fetch: state is set after the await, not synchronously.
    // eslint-disable-next-line react/set-state-in-effect
    void load()
  }, [load])

  const resolve = async () => {
    try {
      await admin.resolve(id)
      await load()
    } catch (err) {
      setError(errorText(err))
    }
  }

  return (
    <div className="admin-page">
      <Link to="/admin/chats" className="link-arrow">
        ← {t('ch.back')}
      </Link>
      {error && <p className="notice">{error}</p>}
      {conv && (
        <>
          <div className="admin-toolbar">
            <h1>
              {conv.clientName ?? `#${conv.id}`}
              <span className="muted small"> · {t(`src.${conv.channel}`)}</span>
            </h1>
            {conv.needsHuman && (
              <button className="btn btn-small" onClick={resolve}>
                {t('ch.resolve')}
              </button>
            )}
          </div>
          {conv.needsHuman && conv.handoffReason && (
            <p className="admin-flag">
              🙋 {t('ch.reason')}: {conv.handoffReason}
            </p>
          )}
          {(conv.clientPhone || conv.clientTelegramUserId) && (
            <p className="small">
              {conv.clientPhone}
              {conv.clientTelegramUserId && (
                <>
                  {conv.clientPhone && ' · '}
                  <a href={`tg://user?id=${conv.clientTelegramUserId}`}>Telegram</a>
                </>
              )}
            </p>
          )}
          <div className="admin-transcript">
            {conv.messages.map((m, i) => (
              <div key={i} className={`bubble ${m.role === 'User' ? 'user' : 'assistant'}`}>
                <span className="admin-bubble-meta">
                  {m.role === 'User' ? t('ch.client') : m.role === 'Staff' ? t('ch.staff') : t('ch.ai')} ·{' '}
                  {formatDateTime(m.atUtc, timeZone, locale)}
                </span>
                {m.text}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
