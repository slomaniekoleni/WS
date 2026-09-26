import type { Lang } from './i18n'

export type DayOfWeek = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

export interface Salon {
  name: string
  address: string
  phone: string
  instagram: string | null
  about: string
  policies: string
  currency: string
  timeZone: string
  country: string
  coverImageUrl: string | null
  minAgeWithGuardian: number
  minAgeSolo: number
  openingHours: { day: DayOfWeek; open: string; close: string }[]
}

export type ServiceKind = 'TattooConsultation' | 'TattooSession' | 'TattooTouchUp' | 'Piercing' | 'JewelryChange'

export interface Service {
  id: number
  kind: ServiceKind
  name: string
  description: string
  durationMinutes: number
  priceFrom: number | null
  priceTo: number | null
  bookableOnline: boolean
  adultsOnly: boolean
  artistIds: number[]
}

export interface Artist {
  id: number
  name: string
  specialty: 'Tattoo' | 'Piercing'
  bio: string
  styles: string[]
  photoUrl: string | null
  instagram: string | null
  portfolio: { url: string; style: string | null; caption: string }[]
  serviceIds: number[]
}

export interface ArtistSlots {
  artistId: number
  artistName: string
  slots: string[]
}

export type AgeGroup = 'Adult' | 'MinorWithGuardian'

export interface TattooDetails {
  idea?: string
  placement?: string
  size?: string
  style?: string
}

export interface CreateBooking {
  serviceId: number
  artistId: number
  startUtc: string
  clientName: string
  phone: string
  email?: string
  ageGroup: AgeGroup
  language: Lang
  notes?: string
  tattoo?: TattooDetails
}

export interface BookingCreated {
  id: number
  status: string
  startUtc: string
  endUtc: string
  artistName: string
  serviceName: string
}

/** Error from the API; `code` is the ProblemDetails type (e.g. "SlotTaken"). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  constructor(status: number, code: string, message: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new ApiError(res.status, body.type ?? String(res.status), body.title ?? res.statusText)
  }
  return res.json() as Promise<T>
}

export const api = {
  salon: (lang: Lang) => request<Salon>(`/api/salon?lang=${lang}`),
  services: (lang: Lang) => request<Service[]>(`/api/services?lang=${lang}`),
  artists: (lang: Lang) => request<Artist[]>(`/api/artists?lang=${lang}`),
  availability: (serviceId: number, artistId: number | null, from: string, to: string) =>
    request<ArtistSlots[]>(
      `/api/availability?serviceId=${serviceId}&from=${from}&to=${to}` + (artistId ? `&artistId=${artistId}` : ''),
    ),
  book: (body: CreateBooking) => request<BookingCreated>('/api/bookings', { method: 'POST', body: JSON.stringify(body) }),
}
