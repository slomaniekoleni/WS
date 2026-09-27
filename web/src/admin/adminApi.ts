import { ApiError, type ArtistSlots, type ServiceKind, type TattooDetails } from '../api'

export type BookingStatus = 'Pending' | 'Confirmed' | 'Declined' | 'Cancelled' | 'Completed' | 'NoShow'
export type Channel = 'Website' | 'WebChat' | 'Telegram' | 'Admin'
export type Specialty = 'Tattoo' | 'Piercing'
export type Day = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'
export const WEEK: Day[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

export interface Me {
  id: number
  login: string
  displayName: string
  role: 'Owner' | 'Artist'
}

export interface AdminBooking {
  id: number
  status: BookingStatus
  startUtc: string
  endUtc: string
  artistId: number
  artistName: string
  serviceId: number
  serviceName: string
  clientId: number
  clientName: string
  phone: string | null
  email: string | null
  telegramUserId: number | null
  source: Channel
  withGuardian: boolean
  needsPrivateRoom: boolean
  tattoo: TattooDetails | null
  clientNotes: string | null
  staffNotes: string | null
  createdAtUtc: string
  declineOrCancelReason: string | null
}

export interface Localized {
  en: string
  ru: string
}

export interface Hours {
  day: Day
  start: string
  end: string
}

export interface TimeOff {
  id: number
  startUtc: string
  endUtc: string
  reason: string | null
}

export interface AdminArtist {
  id: number
  name: string
  specialty: Specialty
  bio: Localized
  styles: string[]
  photoUrl: string | null
  instagramHandle: string | null
  telegramUsername: string | null
  isActive: boolean
  sortOrder: number
  hours: Hours[]
  timeOff: TimeOff[]
  serviceIds: number[]
}

export type ArtistEdit = Omit<AdminArtist, 'id' | 'timeOff'>

export interface AdminService {
  id: number
  kind: ServiceKind
  name: Localized
  description: Localized
  durationMinutes: number
  bufferMinutes: number
  priceFrom: number | null
  priceTo: number | null
  needsPrivateRoom: boolean
  adultsOnly: boolean
  bookableOnline: boolean
  isActive: boolean
  sortOrder: number
  artistIds: number[]
}

export type ServiceEdit = Omit<AdminService, 'id' | 'artistIds'>

export interface ConversationSummary {
  id: number
  channel: Channel
  clientName: string | null
  clientPhone: string | null
  needsHuman: boolean
  handoffReason: string | null
  lastMessageAtUtc: string
  lastMessage: string | null
  messageCount: number
}

export interface Conversation {
  id: number
  channel: Channel
  externalId: string
  language: string
  needsHuman: boolean
  handoffReason: string | null
  clientName: string | null
  clientPhone: string | null
  clientTelegramUserId: number | null
  messages: { role: 'User' | 'Assistant' | 'Staff'; text: string; atUtc: string }[]
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new ApiError(res.status, body.type ?? String(res.status), body.title ?? res.statusText)
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

const post = <T>(path: string, body?: unknown) =>
  call<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
const put = <T>(path: string, body: unknown) => call<T>(path, { method: 'PUT', body: JSON.stringify(body) })

export const admin = {
  me: () => call<Me>('/api/admin/auth/me'),
  login: (login: string, password: string) => post<Me>('/api/admin/auth/login', { login, password }),
  logout: () => post<void>('/api/admin/auth/logout'),

  bookings: (from: string, to: string) => call<AdminBooking[]>(`/api/admin/bookings?from=${from}&to=${to}`),
  decide: (id: number, action: 'approve' | 'decline' | 'cancel' | 'complete' | 'no-show', reason?: string) =>
    post<AdminBooking>(`/api/admin/bookings/${id}/${action}`, { reason }),
  saveNotes: (id: number, staffNotes: string) => put<void>(`/api/admin/bookings/${id}/notes`, { staffNotes }),
  availability: (serviceId: number, artistId: number, date: string) =>
    call<ArtistSlots[]>(`/api/admin/availability?serviceId=${serviceId}&artistId=${artistId}&from=${date}&to=${date}`),
  createBooking: (body: {
    serviceId: number
    artistId: number
    startUtc: string
    clientName: string
    phone?: string
    ageGroup: 'Adult' | 'MinorWithGuardian'
    notes?: string
  }) => post<AdminBooking>('/api/admin/bookings', body),

  artists: () => call<AdminArtist[]>('/api/admin/artists'),
  saveArtist: (id: number | null, edit: ArtistEdit) =>
    id == null ? post<{ id: number }>('/api/admin/artists', edit) : put<{ id: number }>(`/api/admin/artists/${id}`, edit),
  addTimeOff: (artistId: number, startLocal: string, endLocal: string, reason: string) =>
    post<TimeOff>(`/api/admin/artists/${artistId}/time-off`, { startLocal, endLocal, reason }),
  deleteTimeOff: (id: number) => call<void>(`/api/admin/time-off/${id}`, { method: 'DELETE' }),

  services: () => call<AdminService[]>('/api/admin/services'),
  saveService: (id: number | null, edit: ServiceEdit) =>
    id == null ? post<{ id: number }>('/api/admin/services', edit) : put<{ id: number }>(`/api/admin/services/${id}`, edit),

  conversations: (needsHuman: boolean) => call<ConversationSummary[]>(`/api/admin/conversations${needsHuman ? '?needsHuman=true' : ''}`),
  conversation: (id: number) => call<Conversation>(`/api/admin/conversations/${id}`),
  resolve: (id: number) => post<void>(`/api/admin/conversations/${id}/resolve`),
}
