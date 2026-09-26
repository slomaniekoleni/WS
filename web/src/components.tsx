import { useState } from 'react'
import { Link } from 'react-router'
import type { Artist, DayOfWeek, Salon } from './api'
import { shortTime, sized } from './format'
import { Lightbox } from './gallery'
import { useI18n } from './i18n'

const dayIndex: Record<DayOfWeek, number> = {
  Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6,
}

function dayName(day: DayOfWeek, locale: string) {
  // 2026-01-04 is a Sunday.
  return new Date(Date.UTC(2026, 0, 4 + dayIndex[day])).toLocaleDateString(locale, { weekday: 'short', timeZone: 'UTC' })
}

/** Opening hours with consecutive identical days merged ("Mon–Sun 11:00–20:00"). */
export function OpeningHours({ salon }: { salon: Salon }) {
  const { locale, lang } = useI18n()
  const rows: { from: DayOfWeek; to: DayOfWeek; hours: string }[] = []
  for (const h of salon.openingHours) {
    const hours = `${shortTime(h.open)}–${shortTime(h.close)}`
    const last = rows.at(-1)
    if (last && last.hours === hours) last.to = h.day
    else rows.push({ from: h.day, to: h.day, hours })
  }
  if (rows.length === 1 && salon.openingHours.length === 7) {
    return <p className="hours">{lang === 'ru' ? 'Ежедневно' : 'Every day'} {rows[0].hours}</p>
  }
  return (
    <ul className="hours">
      {rows.map((r) => (
        <li key={r.from}>
          <span>{r.from === r.to ? dayName(r.from, locale) : `${dayName(r.from, locale)}–${dayName(r.to, locale)}`}</span>
          <span>{r.hours}</span>
        </li>
      ))}
    </ul>
  )
}

export function ArtistCard({ artist }: { artist: Artist }) {
  const { t, styleName } = useI18n()
  const [open, setOpen] = useState<number | null>(null)
  const photos = artist.portfolio.map((p) => ({ url: p.url, caption: p.caption, credit: artist.name }))
  return (
    <article className="card artist">
      <div className="avatar" aria-hidden>
        {artist.photoUrl ? <img src={artist.photoUrl} alt="" /> : artist.name[0]}
      </div>
      <div className="artist-body">
        <div className="eyebrow">{artist.specialty === 'Tattoo' ? t('artists.tattoo') : t('artists.piercing')}</div>
        <h3>{artist.name}</h3>
        <p className="muted">{artist.bio}</p>
        {artist.specialty === 'Tattoo' && (
          <ul className="tags">
            {artist.styles.map((s) => (
              <li key={s}>{styleName(s)}</li>
            ))}
          </ul>
        )}
        {artist.portfolio.length > 0 && (
          <div className="portfolio">
            {artist.portfolio.slice(0, 3).map((p, i) => (
              <button key={p.url} onClick={() => setOpen(i)} aria-label={p.caption}>
                <img src={sized(p.url, 300)} alt={p.caption} loading="lazy" />
                {i === 2 && artist.portfolio.length > 3 && <span className="more">+{artist.portfolio.length - 3}</span>}
              </button>
            ))}
          </div>
        )}
        {open != null && <Lightbox photos={photos} start={open} onClose={() => setOpen(null)} />}
        <Link to={`/book?artist=${artist.id}`} className="link-arrow">
          {t('artists.book', { name: artist.name })} →
        </Link>
      </div>
    </article>
  )
}
