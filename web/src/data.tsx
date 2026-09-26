import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, type Artist, type Salon, type Service } from './api'
import { useI18n } from './i18n'

interface SalonData {
  salon: Salon
  services: Service[]
  artists: Artist[]
}

type State = { status: 'loading' } | { status: 'error' } | ({ status: 'ready' } & SalonData)

const DataContext = createContext<State>({ status: 'loading' })

/** Salon info, services and artists in the current language; reloaded when the language changes. */
export function DataProvider({ children }: { children: ReactNode }) {
  const { lang } = useI18n()
  const [state, setState] = useState<State>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false
    Promise.all([api.salon(lang), api.services(lang), api.artists(lang)])
      .then(([salon, services, artists]) => !cancelled && setState({ status: 'ready', salon, services, artists }))
      .catch(() => !cancelled && setState({ status: 'error' }))
    return () => {
      cancelled = true
    }
  }, [lang])

  return <DataContext.Provider value={state}>{children}</DataContext.Provider>
}

// eslint-disable-next-line react/only-export-components
export function useData() {
  return useContext(DataContext)
}
