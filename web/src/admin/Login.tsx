import { useState, type FormEvent } from 'react'
import { ApiError } from '../api'
import { Seal } from '../components'
import { admin, type Me } from './adminApi'
import { useAdminT } from './adminI18n'

export default function Login({ onLoggedIn }: { onLoggedIn: (me: Me) => void }) {
  const t = useAdminT()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      onLoggedIn(await admin.login(login, password))
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429 ? t('login.tooMany') : t('login.error'))
      setPassword('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-center">
      <form className="card form admin-login" onSubmit={submit}>
        <div className="admin-login-head">
          <Seal size={56} />
          <h1>{t('login.title')}</h1>
        </div>
        <label>
          {t('login.login')}
          <input value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" required autoFocus />
        </label>
        <label>
          {t('login.password')}
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>
        {error && <p className="notice">{error}</p>}
        <button className="btn" disabled={busy}>
          {t('login.submit')}
        </button>
      </form>
    </div>
  )
}
