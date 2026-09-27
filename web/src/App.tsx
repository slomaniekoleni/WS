import { lazy, Suspense, useEffect } from 'react'
import { Link, NavLink, Route, Routes, useLocation } from 'react-router'
import ChatWidget from './ChatWidget'
import { Seal } from './components'
import { useData } from './data'
import { useI18n, type Key } from './i18n'
import Artists from './pages/Artists'
import Book from './pages/Book'
import Home from './pages/Home'
import Info from './pages/Info'
import Services from './pages/Services'

// Staff panel: its own chunk, so website visitors never download it.
const AdminApp = lazy(() => import('./admin/AdminApp'))

export default function App() {
  const { t } = useI18n()
  const data = useData()
  const { pathname } = useLocation()

  useEffect(() => {
    window.scrollTo(0, 0)
  }, [pathname])

  // Tab title per page, e.g. "Artists · Wise City".
  const page = PAGE_TITLES[pathname.replace(/\/$/, '')]
  const salonName = data.status === 'ready' ? data.salon.name : 'Wise City'
  useEffect(() => {
    if (pathname.startsWith('/admin')) return
    document.title = page ? `${t(page)} · ${salonName}` : `${salonName}: ${t('footer.rights')}`
  }, [pathname, page, salonName, t])

  if (pathname === '/admin' || pathname.startsWith('/admin/')) {
    return (
      <Suspense fallback={null}>
        <Routes>
          <Route path="admin/*" element={<AdminApp />} />
        </Routes>
      </Suspense>
    )
  }

  return (
    <>
      <Header />
      <main>
        {data.status === 'error' && <p className="container notice">{t('common.error')}</p>}
        {data.status === 'ready' && (
          <Routes>
            <Route index element={<Home />} />
            <Route path="services" element={<Services />} />
            <Route path="artists" element={<Artists />} />
            <Route path="info" element={<Info />} />
            <Route path="book" element={<Book />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        )}
      </main>
      <Footer />
      <ChatWidget />
    </>
  )
}

const PAGE_TITLES: Record<string, Key> = {
  '/services': 'nav.services',
  '/artists': 'nav.artists',
  '/info': 'nav.info',
  '/book': 'nav.book',
}

function NotFound() {
  const { t } = useI18n()
  return (
    <section className="container section not-found">
      <h1>{t('notFound.title')}</h1>
      <p className="muted">{t('notFound.text')}</p>
      <div className="not-found-actions">
        <Link to="/" className="btn">
          {t('notFound.home')}
        </Link>
        <Link to="/book" className="btn btn-ghost">
          {t('nav.book')}
        </Link>
      </div>
    </section>
  )
}

function Header() {
  const { t, lang, setLang } = useI18n()
  return (
    <header className="header">
      <div className="container header-inner">
        <Link to="/" className="logo">
          <Seal size={38} />
          <span>Wise City</span>
        </Link>
        <nav className="nav">
          <NavLink to="/services">{t('nav.services')}</NavLink>
          <NavLink to="/artists">{t('nav.artists')}</NavLink>
          <NavLink to="/info">{t('nav.info')}</NavLink>
        </nav>
        <div className="header-actions">
          <button className="lang" onClick={() => setLang(lang === 'ru' ? 'en' : 'ru')} aria-label="Language">
            {lang === 'ru' ? 'EN' : 'RU'}
          </button>
          <Link to="/book" className="btn btn-small">
            {t('nav.book')}
          </Link>
        </div>
      </div>
    </header>
  )
}

function Footer() {
  const { t } = useI18n()
  const data = useData()
  if (data.status !== 'ready') return null
  const { salon } = data
  return (
    <footer className="footer">
      <div className="container footer-inner">
        <div>
          <div className="logo">{salon.name}</div>
          <div className="muted">{t('footer.rights')}</div>
        </div>
        <div className="footer-contacts">
          <span>{salon.address}</span>
          <a href={`tel:${salon.phone.replace(/[^+\d]/g, '')}`}>{salon.phone}</a>
          {salon.instagram && (
            <a href={`https://instagram.com/${salon.instagram}`} target="_blank" rel="noreferrer">
              @{salon.instagram}
            </a>
          )}
          {salon.chatEnabled && salon.telegramBot && (
            <a href={`https://t.me/${salon.telegramBot}`} target="_blank" rel="noreferrer">
              Telegram: @{salon.telegramBot}
            </a>
          )}
        </div>
      </div>
    </footer>
  )
}
