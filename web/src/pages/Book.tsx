import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js/min'
import { Link, useSearchParams } from 'react-router'
import { api, ApiError, type AgeGroup, type ArtistSlots, type BookingCreated, type Service } from '../api'
import { useData } from '../data'
import { addDays, formatDateTime, formatDay, formatDuration, formatPrice, formatTime, isTattoo, salonDate } from '../format'
import { useI18n } from '../i18n'

const DAYS_AHEAD = 14

interface Slot {
  startUtc: string
  artistId: number
}

export default function Book() {
  const { t, lang, locale } = useI18n()
  const data = useData()
  const [params, setParams] = useSearchParams()
  const [slot, setSlot] = useState<Slot | null>(null)
  const [done, setDone] = useState<BookingCreated | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const serviceId = Number(params.get('service')) || null
  const artistId = Number(params.get('artist')) || null
  const kind = params.get('kind')

  const setParam = (key: string, value: number | null) => {
    const next = new URLSearchParams(params)
    if (value == null) next.delete(key)
    else next.set(key, String(value))
    setParams(next, { replace: true })
    setSlot(null)
  }

  if (data.status !== 'ready') return null
  const { salon, services, artists } = data
  const service = services.find((s) => s.id === serviceId && s.bookableOnline) ?? null
  const artist = artists.find((a) => a.id === artistId) ?? null

  if (done) {
    return (
      <div className="container page narrow">
        <div className="card done">
          <h1>{t('book.done.title')}</h1>
          <p className="lead">
            {t('book.summary', {
              service: done.serviceName,
              artist: done.artistName,
              when: formatDateTime(done.startUtc, salon.timeZone, locale),
            })}
          </p>
          <p className="muted">{t('book.done.text', { artist: done.artistName })}</p>
          <Link to="/book" className="btn" onClick={() => setDone(null)}>
            {t('book.done.again')}
          </Link>
        </div>
      </div>
    )
  }

  const bookable = services.filter(
    (s) =>
      s.bookableOnline &&
      (!artist || artist.serviceIds.includes(s.id)) &&
      (kind == null || (kind === 'tattoo') === isTattoo(s)),
  )
  const serviceArtists = service ? artists.filter((a) => a.serviceIds.includes(service.id)) : []

  return (
    <div className="container page narrow">
      <h1>{t('book.title')}</h1>

      <Step title={t('book.step.service')} summary={service?.name} onChange={() => setParam('service', null)}>
        <ul className="options">
          {bookable.map((s) => (
            <li key={s.id}>
              <button className="option" onClick={() => setParam('service', s.id)}>
                <span>{s.name}</span>
                <span className="muted nowrap">
                  {formatDuration(s.durationMinutes, t)} · {formatPrice(s, salon.currency, t)}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </Step>

      {service && (
        <Step title={t('book.step.artist')}>
          <div className="chips">
            {serviceArtists.length > 1 && (
              <button className={`chip ${artistId == null ? 'active' : ''}`} onClick={() => setParam('artist', null)}>
                {t('book.anyArtist')}
              </button>
            )}
            {serviceArtists.map((a) => (
              <button
                key={a.id}
                className={`chip ${artistId === a.id ? 'active' : ''}`}
                onClick={() => setParam('artist', a.id)}
              >
                {a.name}
              </button>
            ))}
          </div>
        </Step>
      )}

      {service && (
        <Step
          title={t('book.step.time')}
          summary={slot ? formatDateTime(slot.startUtc, salon.timeZone, locale) : undefined}
          onChange={() => setSlot(null)}
        >
          {notice && <p className="notice">{notice}</p>}
          <SlotPicker
            key={`-`}
            service={service}
            artistId={serviceArtists.some((a) => a.id === artistId) ? artistId : null}
            timeZone={salon.timeZone}
            onPick={(s) => {
              setNotice(null)
              setSlot(s)
            }}
          />
        </Step>
      )}

      {service && slot && (
        <Step title={t('book.step.details')}>
          <DetailsForm
            service={service}
            slot={slot}
            lang={lang}
            country={salon.country as CountryCode}
            onBooked={setDone}
            onSlotTaken={() => {
              setSlot(null)
              setNotice(t('book.error.taken'))
            }}
          />
        </Step>
      )}
    </div>
  )
}

function Step(props: { title: string; summary?: string; onChange?: () => void; children: ReactNode }) {
  const { t } = useI18n()
  return (
    <section className="card step">
      <div className="step-head">
        <h2>{props.title}</h2>
        {props.summary && props.onChange && (
          <button className="link" onClick={props.onChange}>
            {t('book.change')}
          </button>
        )}
      </div>
      {props.summary ? <p className="step-summary">{props.summary}</p> : props.children}
    </section>
  )
}

function SlotPicker(props: {
  service: Service
  artistId: number | null
  timeZone: string
  onPick: (slot: Slot) => void
}) {
  const { t, locale } = useI18n()
  const { service, artistId, timeZone, onPick } = props
  const [result, setResult] = useState<ArtistSlots[] | null>(null)
  const [error, setError] = useState(false)
  const [day, setDay] = useState<string | null>(null)

  const today = salonDate(new Date(), timeZone)

  useEffect(() => {
    let cancelled = false
    api
      .availability(service.id, artistId, today, addDays(today, DAYS_AHEAD - 1))
      .then((r) => !cancelled && setResult(r))
      .catch(() => !cancelled && setError(true))
    return () => {
      cancelled = true
    }
  }, [service.id, artistId, today])

  // Remounted (key) when service/artist change, so no reset needed here.
  // date -> start time -> first artist free at that time
  const byDay = useMemo(() => {
    const map = new Map<string, Map<string, number>>()
    for (const a of result ?? []) {
      for (const s of a.slots) {
        const date = salonDate(new Date(s), timeZone)
        const times = map.get(date) ?? new Map<string, number>()
        if (!times.has(s)) times.set(s, a.artistId)
        map.set(date, times)
      }
    }
    return map
  }, [result, timeZone])

  const days = Array.from({ length: DAYS_AHEAD }, (_, i) => addDays(today, i))
  const firstFree = days.find((d) => byDay.has(d)) ?? null
  const selectedDay = day && byDay.has(day) ? day : firstFree

  if (error) return <p className="notice">{t('common.error')}</p>
  if (!result) return <p className="muted">{t('book.loading')}</p>
  if (!firstFree) return <p className="muted">{t('book.noSlots')}</p>

  const times = [...(byDay.get(selectedDay!) ?? new Map<string, number>()).entries()].sort(([a], [b]) => a.localeCompare(b))

  return (
    <>
      <div className="days">
        {days.map((d) => {
          const f = formatDay(d, locale)
          return (
            <button
              key={d}
              className={`day ${d === selectedDay ? 'active' : ''}`}
              disabled={!byDay.has(d)}
              onClick={() => setDay(d)}
            >
              <span>{f.weekday}</span>
              <strong>{f.day}</strong>
            </button>
          )
        })}
      </div>
      <div className="times">
        {times.map(([startUtc, aId]) => (
          <button key={startUtc} className="chip" onClick={() => onPick({ startUtc, artistId: aId })}>
            {formatTime(startUtc, timeZone, locale)}
          </button>
        ))}
      </div>
    </>
  )
}

function DetailsForm(props: {
  service: Service
  slot: Slot
  lang: 'en' | 'ru'
  country: CountryCode
  onBooked: (b: BookingCreated) => void
  onSlotTaken: () => void
}) {
  const { t } = useI18n()
  const { service, slot, lang } = props
  const consultation = service.kind === 'TattooConsultation'
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [phoneError, setPhoneError] = useState(false)
  const [email, setEmail] = useState('')
  const [age, setAge] = useState<AgeGroup>('Adult')
  const [notes, setNotes] = useState('')
  const [tattoo, setTattoo] = useState({ idea: '', placement: '', size: '', style: '' })
  const [consent, setConsent] = useState(false)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Numbers without a country code are read as local to the salon ("29 123 45 67", "80291234567").
  const parsePhone = (value: string) => {
    const parsed = parsePhoneNumberFromString(value, props.country)
    return parsed?.isValid() ? parsed : null
  }

  const checkPhone = () => {
    if (!phone.trim()) return
    const parsed = parsePhone(phone)
    setPhoneError(!parsed)
    if (parsed) setPhone(parsed.formatInternational())
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const parsed = parsePhone(phone)
    if (!parsed) {
      setPhoneError(true)
      document.getElementById('phone')?.focus()
      return
    }
    setSending(true)
    setError(null)
    try {
      const booked = await api.book({
        serviceId: service.id,
        artistId: slot.artistId,
        startUtc: slot.startUtc,
        clientName: name.trim(),
        phone: parsed.number,
        email: email.trim() || undefined,
        ageGroup: service.adultsOnly ? 'Adult' : age,
        language: lang,
        notes: notes.trim() || undefined,
        tattoo: consultation ? tattoo : undefined,
      })
      props.onBooked(booked)
    } catch (err) {
      if (err instanceof ApiError && err.code === 'InvalidPhone') {
        setPhoneError(true)
      } else if (err instanceof ApiError && err.code === 'SlotTaken') {
        props.onSlotTaken()
      } else {
        setError(t('book.error.generic'))
      }
    } finally {
      setSending(false)
    }
  }

  const tattooField = (key: keyof typeof tattoo, label: string) => (
    <label>
      {label}
      <input value={tattoo[key]} onChange={(e) => setTattoo({ ...tattoo, [key]: e.target.value })} maxLength={200} />
    </label>
  )

  return (
    <form className="form" onSubmit={submit}>
      <label>
        {t('book.name')}
        <input required value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoComplete="name" />
      </label>
      <label>
        {t('book.phone')}
        <input
          id="phone"
          required
          type="tel"
          inputMode="tel"
          value={phone}
          onChange={(e) => {
            setPhone(e.target.value)
            if (phoneError && parsePhone(e.target.value)) setPhoneError(false)
          }}
          onBlur={checkPhone}
          placeholder="+375 29 123-45-67"
          autoComplete="tel"
          aria-invalid={phoneError}
          aria-describedby={phoneError ? 'phone-error' : undefined}
        />
        {phoneError && (
          <span id="phone-error" className="field-error">
            {t('book.phone.invalid')}
          </span>
        )}
      </label>
      <label>
        {t('book.email')}
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
      </label>

      <fieldset>
        <legend>{t('book.age')}</legend>
        {service.adultsOnly ? (
          <p className="muted">{t('book.age.adultsOnlyNote')}</p>
        ) : (
          <>
            <label className="radio">
              <input type="radio" checked={age === 'Adult'} onChange={() => setAge('Adult')} />
              {t('book.age.adult')}
            </label>
            <label className="radio">
              <input type="radio" checked={age === 'MinorWithGuardian'} onChange={() => setAge('MinorWithGuardian')} />
              {t('book.age.minor')}
            </label>
          </>
        )}
      </fieldset>

      {consultation && (
        <fieldset>
          <legend>{t('book.tattoo.title')}</legend>
          <label>
            {t('book.tattoo.idea')}
            <textarea
              value={tattoo.idea}
              onChange={(e) => setTattoo({ ...tattoo, idea: e.target.value })}
              rows={3}
              maxLength={1000}
            />
          </label>
          <div className="form-row">
            {tattooField('placement', t('book.tattoo.placement'))}
            {tattooField('size', t('book.tattoo.size'))}
          </div>
          {tattooField('style', t('book.tattoo.style'))}
          <p className="muted small">{t('book.tattoo.refs')}</p>
        </fieldset>
      )}

      <label>
        {t('book.notes')}
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={1000} />
      </label>

      <label className="radio">
        <input type="checkbox" required checked={consent} onChange={(e) => setConsent(e.target.checked)} />
        {t('book.consent')}
      </label>

      {error && <p className="notice">{error}</p>}
      <button className="btn" disabled={sending}>
        {sending ? t('book.sending') : t('book.submit')}
      </button>
    </form>
  )
}
