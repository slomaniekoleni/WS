import Markdown from 'react-markdown'
import { OpeningHours } from '../components'
import { useData } from '../data'
import { useI18n } from '../i18n'

export default function Info() {
  const { t } = useI18n()
  const data = useData()
  if (data.status !== 'ready') return null
  const { salon } = data

  return (
    <div className="container page info">
      <div className="prose">
        <h1>{t('info.title')}</h1>
        <p className="lead">{salon.about}</p>
        <Markdown>{salon.policies}</Markdown>
      </div>
      <aside className="info-side">
        <div className="card">
          <h3>{t('info.hours')}</h3>
          <OpeningHours salon={salon} />
        </div>
        <div className="card">
          <h3>{t('info.contacts')}</h3>
          <p>{salon.address}</p>
          <p>
            <a href={`tel:${salon.phone.replace(/[^+\d]/g, '')}`}>{salon.phone}</a>
          </p>
          {salon.instagram && (
            <p>
              <a href={`https://instagram.com/${salon.instagram}`} target="_blank" rel="noreferrer">
                @{salon.instagram}
              </a>
            </p>
          )}
        </div>
      </aside>
    </div>
  )
}
