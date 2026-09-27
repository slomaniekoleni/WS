import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { addDays } from '../format'
import { useI18n } from '../i18n'
import { admin, WEEK, type AdminArtist, type AdminService, type ArtistEdit, type Day, type Hours } from './adminApi'
import { useAdminT } from './adminI18n'
import { Dialog, useErrorText } from './AdminApp'

const EMPTY: ArtistEdit = {
  name: '',
  specialty: 'Tattoo',
  bio: { en: '', ru: '' },
  styles: [],
  photoUrl: null,
  instagramHandle: null,
  telegramUsername: null,
  isActive: true,
  sortOrder: 0,
  hours: [],
  serviceIds: [],
}

export default function ArtistsAdmin({ timeZone }: { timeZone: string }) {
  const t = useAdminT()
  const { lang } = useI18n()
  const [artists, setArtists] = useState<AdminArtist[] | null>(null)
  const [services, setServices] = useState<AdminService[]>([])
  const [editing, setEditing] = useState<AdminArtist | 'new' | null>(null)

  const load = useCallback(async () => {
    const [a, s] = await Promise.all([admin.artists(), admin.services()])
    setArtists(a)
    setServices(s)
  }, [])

  useEffect(() => {
    // Async fetch: state is set after the await, not synchronously.
    // eslint-disable-next-line react/set-state-in-effect
    void load()
  }, [load])

  const serviceName = (s: AdminService) => (lang === 'ru' ? s.name.ru || s.name.en : s.name.en || s.name.ru)

  return (
    <div className="admin-page">
      <div className="admin-toolbar">
        <h1>{t('nav.artists')}</h1>
        <button className="btn btn-small" onClick={() => setEditing('new')}>
          + {t('ar.add')}
        </button>
      </div>
      {!artists && <p className="muted">{t('c.loading')}</p>}
      <div className="admin-list">
        {artists?.map((a) => (
          <button key={a.id} className="card admin-item" onClick={() => setEditing(a)}>
            <strong>{a.name}</strong>
            <span className="muted small">
              {a.specialty === 'Tattoo' ? t('ar.tattoo') : t('ar.piercing')}
              {a.telegramUsername && ` · @${a.telegramUsername}`}
              {!a.isActive && ` · ${t('ar.inactive')}`}
            </span>
            <span className="small">
              {a.hours.map((h) => `${dayShort(h.day, lang)} ${h.start.slice(0, 5)}–${h.end.slice(0, 5)}`).join(', ') || '—'}
            </span>
          </button>
        ))}
      </div>
      {editing && (
        <ArtistDialog
          artist={editing === 'new' ? null : editing}
          services={services}
          serviceName={serviceName}
          timeZone={timeZone}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            void load()
          }}
          onTimeOffChanged={load}
        />
      )}
    </div>
  )
}

function dayShort(day: Day, lang: string) {
  // 2026-01-05 is a Monday.
  const d = new Date(Date.UTC(2026, 0, 5 + WEEK.indexOf(day)))
  return d.toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-GB', { weekday: 'short', timeZone: 'UTC' })
}

function ArtistDialog(props: {
  artist: AdminArtist | null
  services: AdminService[]
  serviceName: (s: AdminService) => string
  timeZone: string
  onClose: () => void
  onSaved: () => void
  onTimeOffChanged: () => Promise<void>
}) {
  const t = useAdminT()
  const { lang, locale } = useI18n()
  const errorText = useErrorText()
  const a = props.artist
  const [edit, setEdit] = useState<ArtistEdit>(() => (a ? { ...a } : { ...EMPTY }))
  const [styles, setStyles] = useState(() => (a?.styles ?? []).join(', '))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const set = <K extends keyof ArtistEdit>(key: K, value: ArtistEdit[K]) => setEdit((e) => ({ ...e, [key]: value }))

  // One shift per weekday in the editor (the backend allows split shifts; we keep the first).
  const shift = (day: Day) => edit.hours.find((h) => h.day === day)
  const setShift = (day: Day, value: Hours | null) =>
    set('hours', [...edit.hours.filter((h) => h.day !== day), ...(value ? [value] : [])])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await admin.saveArtist(a?.id ?? null, {
        ...edit,
        styles: styles.split(',').map((s) => s.trim()).filter(Boolean),
        hours: WEEK.map((d) => shift(d)).filter((h): h is Hours => !!h),
      })
      props.onSaved()
    } catch (err) {
      setError(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title={a ? a.name : t('ar.add')} onClose={props.onClose}>
      <form className="form" onSubmit={submit}>
        <div className="form-row">
          <label>
            {t('ar.name')}
            <input required value={edit.name} onChange={(e) => set('name', e.target.value)} maxLength={100} />
          </label>
          <label>
            {t('ar.specialty')}
            <select value={edit.specialty} onChange={(e) => set('specialty', e.target.value as ArtistEdit['specialty'])}>
              <option value="Tattoo">{t('ar.tattoo')}</option>
              <option value="Piercing">{t('ar.piercing')}</option>
            </select>
          </label>
        </div>
        <label>
          {t('ar.bioRu')}
          <textarea rows={2} value={edit.bio.ru} onChange={(e) => set('bio', { ...edit.bio, ru: e.target.value })} />
        </label>
        <label>
          {t('ar.bioEn')}
          <textarea rows={2} value={edit.bio.en} onChange={(e) => set('bio', { ...edit.bio, en: e.target.value })} />
        </label>
        <label>
          {t('ar.styles')}
          <input value={styles} onChange={(e) => setStyles(e.target.value)} placeholder="fine-line, blackwork" />
        </label>
        <div className="form-row">
          <label>
            {t('ar.telegram')}
            <input
              value={edit.telegramUsername ?? ''}
              onChange={(e) => set('telegramUsername', e.target.value || null)}
              placeholder="@username"
            />
          </label>
          <label>
            {t('ar.instagram')}
            <input value={edit.instagramHandle ?? ''} onChange={(e) => set('instagramHandle', e.target.value || null)} />
          </label>
        </div>
        <div className="form-row">
          <label>
            {t('ar.photo')}
            <input type="url" value={edit.photoUrl ?? ''} onChange={(e) => set('photoUrl', e.target.value || null)} />
          </label>
          <label>
            {t('ar.sort')}
            <input type="number" value={edit.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value))} />
          </label>
        </div>
        <label className="radio">
          <input type="checkbox" checked={edit.isActive} onChange={(e) => set('isActive', e.target.checked)} />
          {t('ar.active')}
        </label>

        <fieldset>
          <legend>{t('ar.hours')}</legend>
          {WEEK.map((day) => {
            const h = shift(day)
            return (
              <div key={day} className="admin-hours-row">
                <label className="radio">
                  <input
                    type="checkbox"
                    checked={!!h}
                    onChange={(e) => setShift(day, e.target.checked ? { day, start: '11:00', end: '20:00' } : null)}
                  />
                  {dayShort(day, lang)}
                </label>
                {h ? (
                  <>
                    <input type="time" value={h.start.slice(0, 5)} onChange={(e) => setShift(day, { ...h, start: e.target.value })} />
                    <span>–</span>
                    <input type="time" value={h.end.slice(0, 5)} onChange={(e) => setShift(day, { ...h, end: e.target.value })} />
                  </>
                ) : (
                  <span className="muted small">{t('ar.off')}</span>
                )}
              </div>
            )
          })}
        </fieldset>

        <fieldset>
          <legend>{t('ar.services')}</legend>
          <div className="admin-checks">
            {props.services.map((s) => (
              <label key={s.id} className="radio">
                <input
                  type="checkbox"
                  checked={edit.serviceIds.includes(s.id)}
                  onChange={(e) =>
                    set('serviceIds', e.target.checked ? [...edit.serviceIds, s.id] : edit.serviceIds.filter((x) => x !== s.id))
                  }
                />
                {props.serviceName(s)}
              </label>
            ))}
          </div>
        </fieldset>

        {error && <p className="notice">{error}</p>}
        <button className="btn" disabled={busy}>
          {t('c.save')}
        </button>
      </form>

      {a && <TimeOffEditor artist={a} timeZone={props.timeZone} locale={locale} onChanged={props.onTimeOffChanged} />}
    </Dialog>
  )
}

function TimeOffEditor(props: { artist: AdminArtist; timeZone: string; locale: string; onChanged: () => Promise<void> }) {
  const t = useAdminT()
  const errorText = useErrorText()
  const [list, setList] = useState(props.artist.timeOff)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const fmt = (utc: string) =>
    new Date(utc).toLocaleDateString(props.locale, { day: 'numeric', month: 'short', timeZone: props.timeZone })

  const add = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      // Whole days in salon time: from 00:00 of the first day to 00:00 after the last day.
      const created = await admin.addTimeOff(props.artist.id, `${from}T00:00`, `${addDays(to || from, 1)}T00:00`, reason)
      setList([...list, created].sort((x, y) => x.startUtc.localeCompare(y.startUtc)))
      setFrom('')
      setTo('')
      setReason('')
      await props.onChanged()
    } catch (err) {
      setError(errorText(err))
    }
  }

  const remove = async (id: number) => {
    try {
      await admin.deleteTimeOff(id)
      setList(list.filter((x) => x.id !== id))
      await props.onChanged()
    } catch (err) {
      setError(errorText(err))
    }
  }

  return (
    <section className="admin-timeoff">
      <h3>{t('ar.timeOff')}</h3>
      {list.length === 0 && <p className="muted small">{t('ar.noTimeOff')}</p>}
      <ul>
        {list.map((x) => (
          <li key={x.id}>
            <span>
              {fmt(x.startUtc)} – {fmt(new Date(new Date(x.endUtc).getTime() - 1).toISOString())}
              {x.reason && <span className="muted"> · {x.reason}</span>}
            </span>
            <button className="link danger small" onClick={() => remove(x.id)}>
              {t('c.delete')}
            </button>
          </li>
        ))}
      </ul>
      <form className="form admin-timeoff-form" onSubmit={add}>
        <div className="form-row">
          <label>
            {t('ar.from')}
            <input type="date" required value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label>
            {t('ar.to')}
            <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </label>
        </div>
        <label>
          {t('ar.reason')}
          <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={200} />
        </label>
        {error && <p className="notice">{error}</p>}
        <button className="btn btn-small btn-ghost">{t('ar.addTimeOff')}</button>
      </form>
    </section>
  )
}
