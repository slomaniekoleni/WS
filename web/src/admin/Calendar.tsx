import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { addDays, formatDateTime, formatDay, formatTime, salonDate } from '../format'
import { useI18n } from '../i18n'
import type { ArtistSlots } from '../api'
import { admin, type AdminArtist, type AdminBooking, type AdminService } from './adminApi'
import { useAdminT } from './adminI18n'
import { Dialog, useErrorText } from './AdminApp'

/** Monday of the week containing the given YYYY-MM-DD date. */
function mondayOf(date: string) {
  const dow = new Date(`${date}T12:00:00Z`).getUTCDay()
  return addDays(date, -((dow + 6) % 7))
}

export default function Calendar({ timeZone }: { timeZone: string }) {
  const t = useAdminT()
  const { locale } = useI18n()
  const today = salonDate(new Date(), timeZone)
  const [week, setWeek] = useState(() => mondayOf(today))
  const [bookings, setBookings] = useState<AdminBooking[] | null>(null)
  const [pending, setPending] = useState<AdminBooking[]>([])
  const [artists, setArtists] = useState<AdminArtist[]>([])
  const [artistId, setArtistId] = useState<number | null>(null)
  const [selected, setSelected] = useState<AdminBooking | null>(null)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState(false)

  const load = useCallback(async () => {
    try {
      const [weekList, upcoming] = await Promise.all([
        admin.bookings(week, addDays(week, 6)),
        admin.bookings(today, addDays(today, 42)),
      ])
      setBookings(weekList)
      setPending(upcoming.filter((b) => b.status === 'Pending'))
      setError(false)
    } catch {
      setError(true)
    }
  }, [week, today])

  useEffect(() => {
    // Async fetch: state is set after the await, not synchronously.
    // eslint-disable-next-line react/set-state-in-effect
    void load()
  }, [load])

  useEffect(() => {
    admin.artists().then(setArtists).catch(() => undefined)
  }, [])

  const days = Array.from({ length: 7 }, (_, i) => addDays(week, i))
  const byDay = useMemo(() => {
    const map = new Map<string, AdminBooking[]>()
    for (const b of bookings ?? []) {
      if (artistId != null && b.artistId !== artistId) continue
      const d = salonDate(new Date(b.startUtc), timeZone)
      map.set(d, [...(map.get(d) ?? []), b])
    }
    return map
  }, [bookings, artistId, timeZone])

  const updated = (b: AdminBooking) => {
    setSelected(b)
    void load()
  }

  return (
    <div className="admin-page">
      <div className="admin-toolbar">
        <div className="admin-week-nav">
          <button onClick={() => setWeek(addDays(week, -7))} aria-label="‹">
            ‹
          </button>
          <button onClick={() => setWeek(mondayOf(today))}>{t('cal.today')}</button>
          <button onClick={() => setWeek(addDays(week, 7))} aria-label="›">
            ›
          </button>
          <strong>
            {formatDay(week, locale).day} – {formatDay(addDays(week, 6), locale).day}
          </strong>
        </div>
        <select value={artistId ?? ''} onChange={(e) => setArtistId(e.target.value ? Number(e.target.value) : null)}>
          <option value="">{t('cal.allArtists')}</option>
          {artists.map((a) => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </select>
        <button className="btn btn-small" onClick={() => setCreating(true)}>
          + {t('cal.newBooking')}
        </button>
      </div>

      {pending.length > 0 && (
        <div className="admin-pending">
          <strong>{t('cal.pending', { n: pending.length })}</strong>
          <div className="admin-pending-list">
            {pending.map((b) => (
              <button key={b.id} className="admin-chip status-Pending" onClick={() => setSelected(b)}>
                {formatDateTime(b.startUtc, timeZone, locale)} · {b.serviceName} · {b.artistName} · {b.clientName}
              </button>
            ))}
          </div>
        </div>
      )}

      {error && <p className="notice">{t('c.error')}</p>}
      {!bookings && !error && <p className="muted">{t('c.loading')}</p>}

      {bookings && (
        <div className="admin-week">
          {days.map((d) => {
            const f = formatDay(d, locale)
            const list = byDay.get(d) ?? []
            return (
              <section key={d} className={`admin-day ${d === today ? 'today' : ''}`}>
                <header>
                  <span>{f.weekday}</span> <strong>{f.day}</strong>
                </header>
                {list.length === 0 && <p className="muted small">{t('cal.empty')}</p>}
                {list.map((b) => (
                  <button key={b.id} className={`admin-booking status-${b.status}`} onClick={() => setSelected(b)}>
                    <span className="time">
                      {formatTime(b.startUtc, timeZone, locale)}–{formatTime(b.endUtc, timeZone, locale)}
                    </span>
                    <span className="what">{b.serviceName}</span>
                    <span className="who">
                      {b.artistName} · {b.clientName}
                    </span>
                    {b.status !== 'Confirmed' && <span className="status">{t(`status.${b.status}`)}</span>}
                  </button>
                ))}
              </section>
            )
          })}
        </div>
      )}

      {selected && (
        <BookingDialog booking={selected} timeZone={timeZone} onClose={() => setSelected(null)} onUpdated={updated} />
      )}
      {creating && (
        <NewBookingDialog
          timeZone={timeZone}
          artists={artists}
          onClose={() => setCreating(false)}
          onCreated={(b) => {
            setCreating(false)
            setWeek(mondayOf(salonDate(new Date(b.startUtc), timeZone)))
            void load()
          }}
        />
      )}
    </div>
  )
}

function BookingDialog(props: {
  booking: AdminBooking
  timeZone: string
  onClose: () => void
  onUpdated: (b: AdminBooking) => void
}) {
  const { booking: b, timeZone } = props
  const t = useAdminT()
  const { locale } = useI18n()
  const errorText = useErrorText()
  const [notes, setNotes] = useState(b.staffNotes ?? '')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [moving, setMoving] = useState(false)

  const act = async (action: 'approve' | 'decline' | 'cancel' | 'complete' | 'no-show') => {
    let reason: string | undefined
    if (action === 'decline' || action === 'cancel') {
      if (action === 'cancel' && !confirm(t('act.confirmCancel'))) return
      reason = prompt(t('act.reason')) ?? undefined
    }
    setBusy(true)
    setMessage(null)
    try {
      props.onUpdated(await admin.decide(b.id, action, reason))
    } catch (err) {
      setMessage(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  const saveNotes = async () => {
    setBusy(true)
    try {
      await admin.saveNotes(b.id, notes)
      setMessage(t('c.saved'))
    } catch (err) {
      setMessage(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  const row = (label: string, value: string | null | undefined) =>
    value ? (
      <div className="admin-row">
        <span className="muted">{label}</span>
        <span>{value}</span>
      </div>
    ) : null

  return (
    <Dialog title={`#${b.id} · ${t(`status.${b.status}`)}`} onClose={props.onClose}>
      {row(t('b.when'), formatDateTime(b.startUtc, timeZone, locale) + '–' + formatTime(b.endUtc, timeZone, locale))}
      {b.status === 'Pending' && b.rescheduledFromUtc && row(t('b.movedFrom'), formatDateTime(b.rescheduledFromUtc, timeZone, locale))}
      {row(t('b.service'), b.serviceName)}
      {row(t('b.artist'), b.artistName)}
      {row(t('b.client'), [b.clientName, b.phone, b.email].filter(Boolean).join(' · '))}
      {b.telegramUserId && (
        <div className="admin-row">
          <span className="muted">Telegram</span>
          <a href={`tg://user?id=${b.telegramUserId}`}>{t('ch.client')}</a>
        </div>
      )}
      {row(t('b.source'), t(`src.${b.source}`))}
      {row(t('b.created'), formatDateTime(b.createdAtUtc, timeZone, locale))}
      {b.withGuardian && <p className="admin-flag">⚠️ {t('b.guardian')}</p>}
      {b.needsPrivateRoom && <p className="admin-flag">🔒 {t('b.privateRoom')}</p>}
      {b.tattoo &&
        row(
          t('b.tattoo'),
          [b.tattoo.idea, b.tattoo.placement, b.tattoo.size, b.tattoo.style].filter(Boolean).join(' · '),
        )}
      {row(t('b.clientNotes'), b.clientNotes)}
      {row(t('b.reason'), b.declineOrCancelReason)}

      <label className="admin-notes">
        {t('b.staffNotes')}
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} />
      </label>
      <button className="btn btn-small btn-ghost" onClick={saveNotes} disabled={busy}>
        {t('b.saveNotes')}
      </button>

      {message && <p className="muted">{message}</p>}

      <div className="admin-actions">
        {b.status === 'Pending' && (
          <>
            <button className="btn btn-small" onClick={() => act('approve')} disabled={busy}>
              {t('act.approve')}
            </button>
            <button className="btn btn-small btn-ghost" onClick={() => act('decline')} disabled={busy}>
              {t('act.decline')}
            </button>
          </>
        )}
        {b.status === 'Confirmed' && (
          <>
            <button className="btn btn-small" onClick={() => act('complete')} disabled={busy}>
              {t('act.complete')}
            </button>
            <button className="btn btn-small btn-ghost" onClick={() => act('no-show')} disabled={busy}>
              {t('act.noShow')}
            </button>
          </>
        )}
        {(b.status === 'Pending' || b.status === 'Confirmed') && (
          <button className="btn btn-small btn-ghost" onClick={() => setMoving(!moving)} disabled={busy}>
            {t('act.move')}
          </button>
        )}
        {(b.status === 'Pending' || b.status === 'Confirmed') && (
          <button className="link danger" onClick={() => act('cancel')} disabled={busy}>
            {t('act.cancel')}
          </button>
        )}
      </div>

      {moving && (
        <MovePanel
          booking={b}
          timeZone={timeZone}
          onMoved={(moved) => {
            setMoving(false)
            props.onUpdated(moved)
          }}
        />
      )}
    </Dialog>
  )
}

/** Staff moves a booking: pick a day, then a free time with any artist who does the service. */
function MovePanel(props: { booking: AdminBooking; timeZone: string; onMoved: (b: AdminBooking) => void }) {
  const { booking: b, timeZone } = props
  const t = useAdminT()
  const { locale } = useI18n()
  const errorText = useErrorText()
  const [date, setDate] = useState(() => salonDate(new Date(b.startUtc), timeZone))
  const [options, setOptions] = useState<ArtistSlots[] | null>(null)
  const [pick, setPick] = useState<{ artistId: number; artistName: string; start: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    admin
      .moveSlots(b.id, date)
      // The booking's own artist first.
      .then((r) => !cancelled && setOptions([...r].sort((x, y) => Number(y.artistId === b.artistId) - Number(x.artistId === b.artistId))))
      .catch(() => !cancelled && setOptions([]))
    return () => {
      cancelled = true
    }
  }, [b.id, b.artistId, date])

  const move = async () => {
    if (!pick) return
    setBusy(true)
    setError(null)
    try {
      props.onMoved(await admin.move(b.id, pick.start, pick.artistId))
    } catch (err) {
      setError(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  const withSlots = options?.filter((o) => o.slots.length > 0) ?? []
  return (
    <section className="admin-move">
      <h3>{t('mv.title')}</h3>
      <p className="muted small">{t('mv.hint')}</p>
      <label className="admin-notes">
        {t('nb.date')}
        <input
          type="date"
          value={date}
          onChange={(e) => {
            setDate(e.target.value)
            setOptions(null)
            setPick(null)
          }}
        />
      </label>
      {options && withSlots.length === 0 && <p className="muted">{t('nb.noSlots')}</p>}
      {withSlots.map((o) => (
        <div key={o.artistId} className="admin-move-artist">
          <strong className="small">
            {o.artistName}
            {o.artistId === b.artistId && <span className="muted"> · {t('mv.current')}</span>}
          </strong>
          <div className="times">
            {o.slots.map((s) => (
              <button
                type="button"
                key={s}
                className={`chip ${pick?.start === s && pick.artistId === o.artistId ? 'active' : ''}`}
                onClick={() => setPick({ artistId: o.artistId, artistName: o.artistName, start: s })}
              >
                {formatTime(s, timeZone, locale)}
              </button>
            ))}
          </div>
        </div>
      ))}
      {error && <p className="notice">{error}</p>}
      {pick && (
        <button className="btn btn-small" onClick={move} disabled={busy}>
          {t('mv.do', { when: formatDateTime(pick.start, timeZone, locale), artist: pick.artistName })}
        </button>
      )}
    </section>
  )
}

function NewBookingDialog(props: {
  timeZone: string
  artists: AdminArtist[]
  onClose: () => void
  onCreated: (b: AdminBooking) => void
}) {
  const t = useAdminT()
  const { lang, locale } = useI18n()
  const errorText = useErrorText()
  const [services, setServices] = useState<AdminService[]>([])
  const [serviceId, setServiceId] = useState<number | null>(null)
  const [artistId, setArtistId] = useState<number | null>(null)
  const [date, setDate] = useState(salonDate(new Date(), props.timeZone))
  const [slots, setSlots] = useState<string[] | null>(null)
  const [start, setStart] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [minor, setMinor] = useState(false)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    admin
      .services()
      .then((s) => setServices(s.filter((x) => x.isActive)))
      .catch(() => undefined)
  }, [])

  const service = services.find((s) => s.id === serviceId)
  const artistChoices = props.artists.filter((a) => a.isActive && service?.artistIds.includes(a.id))

  useEffect(() => {
    if (!serviceId || !artistId || !date) return
    let cancelled = false
    admin
      .availability(serviceId, artistId, date)
      .then((r) => !cancelled && setSlots(r[0]?.slots ?? []))
      .catch(() => !cancelled && setSlots([]))
    return () => {
      cancelled = true
    }
  }, [serviceId, artistId, date])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!serviceId || !artistId || !start) {
      setError(t('nb.pickTime'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      props.onCreated(
        await admin.createBooking({
          serviceId,
          artistId,
          startUtc: start,
          clientName: name.trim(),
          phone: phone.trim() || undefined,
          ageGroup: minor ? 'MinorWithGuardian' : 'Adult',
          notes: notes.trim() || undefined,
        }),
      )
    } catch (err) {
      setError(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('nb.title')} onClose={props.onClose}>
      <form className="form" onSubmit={submit}>
        <label>
          {t('nb.service')}
          <select
            required
            value={serviceId ?? ''}
            onChange={(e) => {
              setServiceId(Number(e.target.value) || null)
              setArtistId(null)
              setSlots(null)
              setStart(null)
            }}
          >
            <option value="" />
            {services.map((s) => (
              <option key={s.id} value={s.id}>
                {lang === 'ru' ? s.name.ru || s.name.en : s.name.en || s.name.ru} ({s.durationMinutes}′)
              </option>
            ))}
          </select>
        </label>
        <div className="form-row">
          <label>
            {t('nb.artist')}
            <select
              required
              value={artistId ?? ''}
              onChange={(e) => {
                setArtistId(Number(e.target.value) || null)
                setSlots(null)
                setStart(null)
              }}
            >
              <option value="" />
              {artistChoices.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t('nb.date')}
            <input
              type="date"
              required
              value={date}
              onChange={(e) => {
                setDate(e.target.value)
                setSlots(null)
                setStart(null)
              }}
            />
          </label>
        </div>
        {slots && (
          <fieldset>
            <legend>{t('nb.time')}</legend>
            {slots.length === 0 ? (
              <p className="muted">{t('nb.noSlots')}</p>
            ) : (
              <div className="times">
                {slots.map((s) => (
                  <button type="button" key={s} className={`chip ${start === s ? 'active' : ''}`} onClick={() => setStart(s)}>
                    {formatTime(s, props.timeZone, locale)}
                  </button>
                ))}
              </div>
            )}
          </fieldset>
        )}
        <div className="form-row">
          <label>
            {t('nb.clientName')}
            <input required value={name} onChange={(e) => setName(e.target.value)} maxLength={100} />
          </label>
          <label>
            {t('nb.phone')}
            <input type="tel" required value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+375 29 123-45-67" />
          </label>
        </div>
        <label className="radio">
          <input type="checkbox" checked={minor} onChange={(e) => setMinor(e.target.checked)} />
          {t('nb.minor')}
        </label>
        <label>
          {t('nb.notes')}
          <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} />
        </label>
        {error && <p className="notice">{error}</p>}
        <button className="btn" disabled={busy}>
          {t('nb.create')}
        </button>
      </form>
    </Dialog>
  )
}
