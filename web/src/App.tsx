import { useEffect } from 'react'
import { Link, NavLink, Route, Routes, useLocation } from 'react-router'
import ChatWidget from './ChatWidget'
import { Seal } from './components'
import { useData } from './data'
import { useI18n } from './i18n'
import Artists from './pages/Artists'
import Book from './pages/Book'
import Home from './pages/Home'
import Info from './pages/Info'
import Services from './pages/Services'

export default function App() {
  const { t } = useI18n()
  const data = useData()
  const { pathname } = useLocation()

  useEffect(() => {
    window.scrollTo(0, 0)
  }, [pathname])

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
            <Route path="*" element={<Home />} />
          </Routes>
        )}
      </main>
      <Footer />
      <ChatWidget />
    </>
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
        </div>
      </div>
    </footer>
  )
}
