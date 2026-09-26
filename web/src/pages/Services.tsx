import { Link } from 'react-router'
import { useData } from '../data'
import { formatDuration, formatPrice, isTattoo } from '../format'
import { useI18n } from '../i18n'

export default function Services() {
  const { t } = useI18n()
  const data = useData()
  if (data.status !== 'ready') return null

  const groups = [
    { title: t('services.tattoo'), items: data.services.filter(isTattoo), note: t('services.note') },
    { title: t('services.piercing'), items: data.services.filter((s) => !isTattoo(s)) },
  ]

  return (
    <div className="container page">
      <h1>{t('services.title')}</h1>
      {groups.map((g) => (
        <section key={g.title} className="section">
          <h2>{g.title}</h2>
          {g.note && <p className="muted">{g.note}</p>}
          <ul className="price-list">
            {g.items.map((s) => (
              <li key={s.id}>
                <div className="price-name">
                  <span>{s.name}</span>
                  {s.adultsOnly && <span className="badge">{t('services.adultsOnly')}</span>}
                  {s.description && <small className="muted">{s.description}</small>}
                </div>
                <span className="muted nowrap">{formatDuration(s.durationMinutes, t)}</span>
                <span className="price nowrap">{formatPrice(s, data.salon.currency, t)}</span>
                {s.bookableOnline ? (
                  <Link to={`/book?service=${s.id}`} className="btn btn-small btn-ghost">
                    {t('services.book')}
                  </Link>
                ) : (
                  <small className="muted">{t('services.afterConsultation')}</small>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}
