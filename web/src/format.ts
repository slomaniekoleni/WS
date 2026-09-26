import type { Service } from './api'

type T = (key: 'services.free' | 'services.from' | 'services.min' | 'services.h') => string

export function formatPrice(s: Pick<Service, 'priceFrom' | 'priceTo'>, currency: string, t: T): string {
  if (s.priceFrom == null) return t('services.free')
  if (s.priceTo == null || s.priceTo === s.priceFrom) return `${s.priceFrom} ${currency}`
  return `${s.priceFrom}–${s.priceTo} ${currency}`
}

export function formatDuration(minutes: number, t: T): string {
  if (minutes < 60) return `${minutes} ${t('services.min')}`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m ? `${h} ${t('services.h')} ${m} ${t('services.min')}` : `${h} ${t('services.h')}`
}

/** YYYY-MM-DD of an instant in the salon's time zone. */
export function salonDate(instant: Date, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(instant)
}

/** YYYY-MM-DD plus n days (calendar arithmetic, no time zone involved). */
export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

export function formatDay(date: string, locale: string): { weekday: string; day: string } {
  const d = new Date(`${date}T12:00:00Z`)
  return {
    weekday: d.toLocaleDateString(locale, { weekday: 'short', timeZone: 'UTC' }),
    day: d.toLocaleDateString(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }),
  }
}

export function formatTime(instant: string, timeZone: string, locale: string): string {
  return new Date(instant).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', timeZone })
}

export function formatDateTime(instant: string, timeZone: string, locale: string): string {
  return new Date(instant).toLocaleString(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    hour: '2-digit',
    minute: '2-digit',
    timeZone,
  })
}

export const isTattoo = (s: Pick<Service, 'kind'>) => s.kind.startsWith('Tattoo')

/** "11:00:00" -> "11:00" */
export const shortTime = (time: string) => time.slice(0, 5)
