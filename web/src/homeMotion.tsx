import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import type { Artist } from './api'
import { sized } from './format'
import { Lightbox, type Photo } from './gallery'
import { useI18n } from './i18n'

// Home page motion (owner's pick 2026-09-27: concept A opening + concept C gallery and artists).
// Plain CSS + a small scroll handler, no animation library. Reduced motion and phones get static/native versions.

const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches

/** Opening: the logo's own ensō is painted in by a brush sweep, headline lines rise, a red stroke draws itself. */
export function HeroInk() {
  const { t } = useI18n()
  const mark = useRef<HTMLDivElement>(null)

  // The seal drifts down and turns a little as the hero scrolls away.
  useEffect(() => {
    if (reducedMotion()) return
    let frame = 0
    const onScroll = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => {
        const y = Math.min(window.scrollY, window.innerHeight)
        if (mark.current) mark.current.style.transform = `translateY(${y * 0.18}px) rotate(${y * 0.008}deg)`
      })
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', onScroll)
      cancelAnimationFrame(frame)
    }
  }, [])

  return (
    <section className="hero hero-ink">
      <div className="container hero-inner">
        <div className="hero-text">
          <p className="eyebrow">{t('home.tagline')}</p>
          <h1 className="rise">
            <span className="line">
              <span>{t('home.h1.1')}</span>
            </span>
            <span className="line">
              <span>{t('home.h1.2')}</span>
            </span>
            <span className="line">
              <span>
                <em>{t('home.h1.3')}</em>
              </span>
            </span>
          </h1>
          <svg className="brush-draw" viewBox="0 0 360 22" aria-hidden>
            <path d="M4 14 C 80 4, 180 20, 356 9" />
          </svg>
          <p className="lead">{t('home.lead')}</p>
          <div className="hero-actions">
            <Link to="/book" className="btn">
              {t('home.cta.book')}
            </Link>
            <a href="#works" className="btn btn-ghost">
              {t('home.cta.works')}
            </a>
          </div>
        </div>
        <div className="hero-mark" aria-hidden>
          <div className="hero-mark-inner" ref={mark}>
            <div className="hero-disc" />
            <div className="hero-logo" />
            <span className="kanji-vertical" lang="ja">
              達磨
            </span>
          </div>
        </div>
      </div>
      <div className="scroll-hint" aria-hidden>
        {t('home.scroll')}
      </div>
    </section>
  )
}

/** Endless line of the styles the salon does. */
export function StyleMarquee({ artists }: { artists: Artist[] }) {
  const { t, styleName } = useI18n()
  const styles = [...new Set(artists.flatMap((a) => a.styles))].map(styleName)
  const items = [...styles, t('home.piercingStyle')]
  return (
    <div className="marquee" aria-hidden>
      <div className="marquee-track">
        {[...items, ...items].map((s, i) => (
          <span key={i}>{s}</span>
        ))}
      </div>
    </div>
  )
}

/**
 * Portfolio that slides sideways while the page scrolls down (the section is pinned).
 * Phones and reduced motion: a normal horizontal swipe strip.
 */
export function WorksGallery({ photos }: { photos: Photo[] }) {
  const { t } = useI18n()
  const outer = useRef<HTMLElement>(null)
  const track = useRef<HTMLDivElement>(null)
  const bar = useRef<HTMLElement>(null)
  const [open, setOpen] = useState<number | null>(null)
  const [pinned, setPinned] = useState(false)

  useEffect(() => {
    const wide = matchMedia('(min-width: 861px)')
    const update = () => setPinned(wide.matches && !reducedMotion())
    update()
    wide.addEventListener('change', update)
    return () => wide.removeEventListener('change', update)
  }, [])

  useEffect(() => {
    const section = outer.current
    const strip = track.current
    if (!pinned || !section || !strip) return
    let distance = 0
    let frame = 0

    const layout = () => {
      distance = Math.max(0, strip.scrollWidth - window.innerWidth)
      // Scrolling this extra height moves the strip its full width.
      section.style.height = `${window.innerHeight + distance}px`
      move()
    }
    const move = () => {
      const top = section.getBoundingClientRect().top
      const p = distance > 0 ? Math.min(1, Math.max(0, -top / distance)) : 0
      strip.style.transform = `translateX(${-p * distance}px)`
      strip.style.setProperty('--zoom', String(1.12 - 0.12 * p))
      if (bar.current) bar.current.style.width = `${p * 100}%`
    }
    const onScroll = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(move)
    }

    const ro = new ResizeObserver(layout)
    ro.observe(strip)
    window.addEventListener('resize', layout)
    window.addEventListener('scroll', onScroll, { passive: true })
    layout()
    return () => {
      ro.disconnect()
      window.removeEventListener('resize', layout)
      window.removeEventListener('scroll', onScroll)
      cancelAnimationFrame(frame)
      section.style.height = ''
      strip.style.transform = ''
    }
  }, [pinned, photos.length])

  return (
    <section className={`works ${pinned ? 'pinned' : ''}`} id="works" ref={outer}>
      <div className="works-sticky">
        <div className="container works-head">
          <h2>{t('home.works')}</h2>
          <p>{pinned ? t('home.worksHint') : t('home.worksSwipe')}</p>
        </div>
        <div className="works-track" ref={track}>
          {photos.map((p, i) => (
            <figure key={p.url}>
              <button className="works-ph" onClick={() => setOpen(i)} aria-label={p.caption}>
                {/* Not lazy: the strip moves by transform, which lazy loading doesn't always notice in time. */}
                <img src={sized(p.url, 700)} alt={p.caption} decoding="async" />
              </button>
              <figcaption>
                <b>{p.credit}</b>
                <span>{p.caption}</span>
              </figcaption>
            </figure>
          ))}
        </div>
        {pinned && (
          <div className="works-progress">
            <i ref={bar} />
          </div>
        )}
      </div>
      {open != null && <Lightbox photos={photos} start={open} onClose={() => setOpen(null)} />}
    </section>
  )
}

/** Artists as big names; hovering one shows their work next to the cursor (touch devices get a thumbnail). */
export function ArtistList({ artists }: { artists: Artist[] }) {
  const { t, styleName } = useI18n()
  const peek = useRef<HTMLDivElement>(null)
  const [peekUrl, setPeekUrl] = useState<string | null>(null)

  // The preview sits mid-row (between the name and the styles) and trails the cursor up and down.
  useEffect(() => {
    if (!matchMedia('(hover: hover)').matches) return
    const target = { x: 0, y: 0 }
    const pos = { x: 0, y: 0 }
    let frame = 0
    const onMove = (e: PointerEvent) => {
      target.x = e.clientX
      target.y = e.clientY
    }
    const tick = () => {
      pos.x += (window.innerWidth * 0.52 - pos.x) * 0.16
      pos.y += (target.y - pos.y) * 0.16
      if (peek.current) peek.current.style.translate = `${pos.x}px ${pos.y}px`
      frame = requestAnimationFrame(tick)
    }
    window.addEventListener('pointermove', onMove, { passive: true })
    frame = requestAnimationFrame(tick)
    return () => {
      window.removeEventListener('pointermove', onMove)
      cancelAnimationFrame(frame)
    }
  }, [])

  return (
    <div className="artist-list">
      {artists.map((a, i) => {
        const cover = a.portfolio[0]?.url ?? a.photoUrl
        const what = a.specialty === 'Tattoo' ? a.styles.map(styleName).join(', ') : t('artists.piercing')
        return (
          <Link
            key={a.id}
            to={`/book?artist=${a.id}`}
            className="artist-row"
            onPointerEnter={() => cover && setPeekUrl(sized(cover, 520))}
            onPointerLeave={() => setPeekUrl(null)}
          >
            <span className="artist-num">{String(i + 1).padStart(2, '0')}</span>
            <h3>{a.name}</h3>
            <span className="artist-what">
              {what}
              <span className="artist-go">{t('artists.book', { name: a.name })} →</span>
            </span>
            {cover && <img className="artist-thumb" src={sized(cover, 200)} alt="" loading="lazy" />}
          </Link>
        )
      })}
      <div className={`artist-peek ${peekUrl ? 'on' : ''}`} ref={peek} aria-hidden>
        {peekUrl && <img src={peekUrl} alt="" />}
      </div>
      <Link to="/artists" className="link-arrow artist-all">
        {t('home.allArtists')} →
      </Link>
    </div>
  )
}

