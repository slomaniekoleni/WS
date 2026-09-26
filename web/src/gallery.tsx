import { useEffect, useRef, useState } from 'react'
import { sized } from './format'
import { useI18n } from './i18n'

export interface Photo {
  url: string
  caption: string
  /** Shown under the caption, e.g. the artist's name. */
  credit?: string
}

/** Horizontal scroll-snap carousel (swipe on phones, arrows on desktop). Click a photo to open it full screen. */
export function Carousel({ photos }: { photos: Photo[] }) {
  const { lang } = useI18n()
  const track = useRef<HTMLDivElement>(null)
  const [edges, setEdges] = useState({ start: true, end: false })
  const [open, setOpen] = useState<number | null>(null)

  const updateEdges = () => {
    const el = track.current
    if (!el) return
    setEdges({ start: el.scrollLeft < 8, end: el.scrollLeft + el.clientWidth > el.scrollWidth - 8 })
  }

  useEffect(() => {
    updateEdges()
    window.addEventListener('resize', updateEdges)
    return () => window.removeEventListener('resize', updateEdges)
  }, [photos.length])

  const scroll = (dir: 1 | -1) => {
    const el = track.current
    const item = el?.firstElementChild as HTMLElement | null
    if (!el || !item) return
    el.scrollBy({ left: dir * (item.offsetWidth + 12) * Math.max(1, Math.floor(el.clientWidth / item.offsetWidth) - 1), behavior: 'smooth' })
  }

  return (
    <div className="carousel">
      <div className="carousel-track" ref={track} onScroll={updateEdges}>
        {photos.map((p, i) => (
          <figure key={p.url} className="carousel-item">
            <button onClick={() => setOpen(i)} aria-label={p.caption}>
              <img src={sized(p.url, 600)} alt={p.caption} loading="lazy" />
            </button>
            <figcaption>
              <span>{p.caption}</span>
              {p.credit && <span className="muted">{p.credit}</span>}
            </figcaption>
          </figure>
        ))}
      </div>
      {!edges.start && (
        <button className="carousel-arrow prev" onClick={() => scroll(-1)} aria-label={lang === 'ru' ? 'Назад' : 'Previous'}>
          ‹
        </button>
      )}
      {!edges.end && (
        <button className="carousel-arrow next" onClick={() => scroll(1)} aria-label={lang === 'ru' ? 'Вперёд' : 'Next'}>
          ›
        </button>
      )}
      {open != null && <Lightbox photos={photos} start={open} onClose={() => setOpen(null)} />}
    </div>
  )
}

/** Full-screen viewer: arrows / keyboard to navigate, Esc or backdrop click to close. */
export function Lightbox({ photos, start, onClose }: { photos: Photo[]; start: number; onClose: () => void }) {
  const [index, setIndex] = useState(start)
  const photo = photos[index]
  const go = (dir: number) => setIndex((i) => (i + dir + photos.length) % photos.length)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowRight') setIndex((i) => (i + 1) % photos.length)
      if (e.key === 'ArrowLeft') setIndex((i) => (i - 1 + photos.length) % photos.length)
    }
    window.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
    }
  }, [onClose, photos.length])

  return (
    <div className="lightbox" role="dialog" aria-modal onClick={onClose}>
      <figure onClick={(e) => e.stopPropagation()}>
        <img src={sized(photo.url, 1600)} alt={photo.caption} />
        <figcaption>
          {photo.caption}
          {photo.credit && <span className="muted"> · {photo.credit}</span>}
          <span className="muted lightbox-count">
            {index + 1} / {photos.length}
          </span>
        </figcaption>
      </figure>
      {photos.length > 1 && (
        <>
          <button className="lightbox-arrow prev" onClick={(e) => (e.stopPropagation(), go(-1))} aria-label="Previous">
            ‹
          </button>
          <button className="lightbox-arrow next" onClick={(e) => (e.stopPropagation(), go(1))} aria-label="Next">
            ›
          </button>
        </>
      )}
      <button className="lightbox-close" onClick={onClose} aria-label="Close">
        ×
      </button>
    </div>
  )
}
