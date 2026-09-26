import { Link } from 'react-router'
import { ArtistCard, OpeningHours } from '../components'
import { useData } from '../data'
import { useI18n } from '../i18n'

export default function Home() {
  const { t } = useI18n()
  const data = useData()
  if (data.status !== 'ready') return null
  const { salon, artists } = data

  return (
    <>
      <section className="hero">
        <div className="container">
          <p className="eyebrow">{t('home.tagline')}</p>
          <h1>{salon.name}</h1>
          <p className="lead">{t('home.lead')}</p>
          <div className="hero-actions">
            <Link to="/book" className="btn">
              {t('home.cta.book')}
            </Link>
            <Link to="/services" className="btn btn-ghost">
              {t('home.cta.services')}
            </Link>
          </div>
        </div>
      </section>

      <section className="container features">
        <Link to="/book?kind=tattoo" className="card feature">
          <h3>{t('home.tattoo.title')}</h3>
          <p className="muted">{t('home.tattoo.text')}</p>
        </Link>
        <Link to="/book?kind=piercing" className="card feature">
          <h3>{t('home.piercing.title')}</h3>
          <p className="muted">{t('home.piercing.text')}</p>
        </Link>
        <Link to="/info" className="card feature">
          <h3>{t('home.age.title')}</h3>
          <p className="muted">{t('home.age.text')}</p>
        </Link>
      </section>

      <section className="container section">
        <h2>{t('home.artists')}</h2>
        <div className="grid">
          {artists.map((a) => (
            <ArtistCard key={a.id} artist={a} />
          ))}
        </div>
      </section>

      <section className="container section visit">
        <div>
          <h2>{t('home.visit')}</h2>
          <p className="lead">{salon.address}</p>
          <a className="lead" href={`tel:${salon.phone.replace(/[^+\d]/g, '')}`}>
            {salon.phone}
          </a>
        </div>
        <div className="card">
          <h3>{t('info.hours')}</h3>
          <OpeningHours salon={salon} />
        </div>
      </section>
    </>
  )
}
