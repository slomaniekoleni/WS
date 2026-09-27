import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { ServiceKind } from '../api'
import { useI18n } from '../i18n'
import { admin, type AdminService, type ServiceEdit } from './adminApi'
import { useAdminT } from './adminI18n'
import { Dialog, useErrorText } from './AdminApp'

const KINDS: ServiceKind[] = ['TattooConsultation', 'TattooSession', 'TattooTouchUp', 'Piercing', 'JewelryChange']

const EMPTY: ServiceEdit = {
  kind: 'Piercing',
  name: { en: '', ru: '' },
  description: { en: '', ru: '' },
  durationMinutes: 30,
  bufferMinutes: 15,
  priceFrom: null,
  priceTo: null,
  needsPrivateRoom: false,
  adultsOnly: false,
  bookableOnline: true,
  isActive: true,
  sortOrder: 0,
}

export default function ServicesAdmin({ currency }: { currency: string }) {
  const t = useAdminT()
  const { lang } = useI18n()
  const [services, setServices] = useState<AdminService[] | null>(null)
  const [editing, setEditing] = useState<AdminService | 'new' | null>(null)

  const load = useCallback(async () => setServices(await admin.services()), [])
  useEffect(() => {
    // Async fetch: state is set after the await, not synchronously.
    // eslint-disable-next-line react/set-state-in-effect
    void load()
  }, [load])

  const price = (s: AdminService) =>
    s.priceFrom == null ? t('sv.free') : s.priceTo == null ? `${s.priceFrom} ${currency}` : `${s.priceFrom}–${s.priceTo} ${currency}`

  return (
    <div className="admin-page">
      <div className="admin-toolbar">
        <h1>{t('nav.services')}</h1>
        <button className="btn btn-small" onClick={() => setEditing('new')}>
          + {t('sv.add')}
        </button>
      </div>
      {!services && <p className="muted">{t('c.loading')}</p>}
      <div className="admin-table">
        {services?.map((s) => (
          <button key={s.id} className={`admin-table-row ${s.isActive ? '' : 'inactive'}`} onClick={() => setEditing(s)}>
            <span>
              {lang === 'ru' ? s.name.ru || s.name.en : s.name.en || s.name.ru}
              <span className="muted small"> · {t(`kind.${s.kind}`)}</span>
            </span>
            <span className="muted">{s.durationMinutes}′</span>
            <span>{price(s)}</span>
            <span className="small muted">
              {[s.bookableOnline ? '🌐' : '', s.adultsOnly ? '18+' : '', s.needsPrivateRoom ? '🔒' : ''].filter(Boolean).join(' ')}
            </span>
          </button>
        ))}
      </div>
      {editing && (
        <ServiceDialog
          service={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            void load()
          }}
        />
      )}
    </div>
  )
}

function ServiceDialog(props: { service: AdminService | null; onClose: () => void; onSaved: () => void }) {
  const t = useAdminT()
  const errorText = useErrorText()
  const s = props.service
  const [edit, setEdit] = useState<ServiceEdit>(() => (s ? { ...s } : { ...EMPTY }))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const set = <K extends keyof ServiceEdit>(key: K, value: ServiceEdit[K]) => setEdit((e) => ({ ...e, [key]: value }))
  const num = (v: string) => (v.trim() === '' ? null : Number(v))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await admin.saveService(s?.id ?? null, edit)
      props.onSaved()
    } catch (err) {
      setError(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  const check = (key: 'needsPrivateRoom' | 'adultsOnly' | 'bookableOnline' | 'isActive', label: string) => (
    <label className="radio">
      <input type="checkbox" checked={edit[key]} onChange={(e) => set(key, e.target.checked)} />
      {label}
    </label>
  )

  return (
    <Dialog title={s ? s.name.ru || s.name.en : t('sv.add')} onClose={props.onClose}>
      <form className="form" onSubmit={submit}>
        <div className="form-row">
          <label>
            {t('sv.nameRu')}
            <input value={edit.name.ru} onChange={(e) => set('name', { ...edit.name, ru: e.target.value })} />
          </label>
          <label>
            {t('sv.nameEn')}
            <input value={edit.name.en} onChange={(e) => set('name', { ...edit.name, en: e.target.value })} />
          </label>
        </div>
        <label>
          {t('sv.descRu')}
          <textarea rows={2} value={edit.description.ru} onChange={(e) => set('description', { ...edit.description, ru: e.target.value })} />
        </label>
        <label>
          {t('sv.descEn')}
          <textarea rows={2} value={edit.description.en} onChange={(e) => set('description', { ...edit.description, en: e.target.value })} />
        </label>
        <label>
          {t('sv.kind')}
          <select value={edit.kind} onChange={(e) => set('kind', e.target.value as ServiceKind)}>
            {KINDS.map((k) => (
              <option key={k} value={k}>
                {t(`kind.${k}`)}
              </option>
            ))}
          </select>
        </label>
        <div className="form-row">
          <label>
            {t('sv.duration')}
            <input type="number" min={5} max={720} required value={edit.durationMinutes} onChange={(e) => set('durationMinutes', Number(e.target.value))} />
          </label>
          <label>
            {t('sv.buffer')}
            <input type="number" min={0} max={240} required value={edit.bufferMinutes} onChange={(e) => set('bufferMinutes', Number(e.target.value))} />
          </label>
        </div>
        <div className="form-row">
          <label>
            {t('sv.priceFrom')}
            <input type="number" min={0} step="0.01" value={edit.priceFrom ?? ''} onChange={(e) => set('priceFrom', num(e.target.value))} />
          </label>
          <label>
            {t('sv.priceTo')}
            <input type="number" min={0} step="0.01" value={edit.priceTo ?? ''} onChange={(e) => set('priceTo', num(e.target.value))} />
          </label>
        </div>
        {check('bookableOnline', t('sv.online'))}
        {check('adultsOnly', t('sv.adultsOnly'))}
        {check('needsPrivateRoom', t('sv.privateRoom'))}
        {check('isActive', t('sv.active'))}
        {error && <p className="notice">{error}</p>}
        <button className="btn" disabled={busy}>
          {t('c.save')}
        </button>
      </form>
    </Dialog>
  )
}
