import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, Navigate, Route, Routes, useNavigate } from 'react-router'
import { ApiError } from '../api'
import { Seal } from '../components'
import { useData } from '../data'
import { useI18n } from '../i18n'
import { admin, type Me } from './adminApi'
import { useAdminT } from './adminI18n'
import ArtistsAdmin from './ArtistsAdmin'
import Calendar from './Calendar'
import Chats from './Chats'
import Login from './Login'
import ServicesAdmin from './ServicesAdmin'
import './admin.css'

/** Staff panel at /admin: login gate, sidebar, sections. */
export default function AdminApp() {
  const [me, setMe] = useState<Me | null | undefined>(undefined)
  const data = useData()

  useEffect(() => {
    admin
      .me()
      .then(setMe)
      .catch(() => setMe(null))
  }, [])

  if (me === undefined || data.status === 'loading') return <div className="admin-center muted">…</div>
  if (me === null) return <Login onLoggedIn={setMe} />
  if (data.status === 'error') return <div className="admin-center notice">API error</div>

  return (
    <Shell me={me} onLoggedOut={() => setMe(null)}>
      <Routes>
        <Route index element={<Calendar timeZone={data.salon.timeZone} />} />
        <Route path="artists" element={<ArtistsAdmin timeZone={data.salon.timeZone} />} />
        <Route path="services" element={<ServicesAdmin currency={data.salon.currency} />} />
        <Route path="chats" element={<Chats timeZone={data.salon.timeZone} />} />
        <Route path="chats/:id" element={<Chats timeZone={data.salon.timeZone} />} />
        <Route path="*" element={<Navigate to="/admin" replace />} />
      </Routes>
    </Shell>
  )
}

function Shell({ me, onLoggedOut, children }: { me: Me; onLoggedOut: () => void; children: ReactNode }) {
  const t = useAdminT()
  const { lang, setLang } = useI18n()
  const navigate = useNavigate()

  const logout = async () => {
    await admin.logout().catch(() => undefined)
    onLoggedOut()
    navigate('/admin')
  }

  return (
    <div className="admin">
      <aside className="admin-side">
        <Link to="/admin" className="admin-brand">
          <Seal size={34} />
          <span>Wise City</span>
        </Link>
        <nav>
          <NavLink to="/admin" end>
            {t('nav.calendar')}
          </NavLink>
          <NavLink to="/admin/chats">{t('nav.chats')}</NavLink>
          <NavLink to="/admin/artists">{t('nav.artists')}</NavLink>
          <NavLink to="/admin/services">{t('nav.services')}</NavLink>
        </nav>
        <div className="admin-side-foot">
          <span className="muted small">{me.displayName}</span>
          <div className="admin-side-actions">
            <button className="lang" onClick={() => setLang(lang === 'ru' ? 'en' : 'ru')}>
              {lang === 'ru' ? 'EN' : 'RU'}
            </button>
            <a href="/" className="muted small">
              {t('nav.site')}
            </a>
            <button className="link small" onClick={logout}>
              {t('nav.logout')}
            </button>
          </div>
        </div>
      </aside>
      <main className="admin-main">{children}</main>
    </div>
  )
}

/** Friendly text for an API failure in the admin panel. */
// eslint-disable-next-line react/only-export-components
export function useErrorText() {
  const t = useAdminT()
  return useCallback((err: unknown) => (err instanceof ApiError && err.status === 409 ? t('c.conflict') : t('c.error')), [t])
}

/** Centered modal dialog; closes on backdrop click or Esc. */
export function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const t = useAdminT()
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="admin-dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <section className="admin-dialog" role="dialog" aria-modal aria-label={title}>
        <header>
          <h2>{title}</h2>
          <button className="admin-x" onClick={onClose} aria-label={t('c.close')}>
            ×
          </button>
        </header>
        <div className="admin-dialog-body">{children}</div>
      </section>
    </div>
  )
}
