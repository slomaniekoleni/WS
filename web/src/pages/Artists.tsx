import { ArtistCard } from '../components'
import { useData } from '../data'
import { useI18n } from '../i18n'

export default function Artists() {
  const { t } = useI18n()
  const data = useData()
  if (data.status !== 'ready') return null

  return (
    <div className="container page">
      <h1>{t('artists.title')}</h1>
      <div className="grid">
        {data.artists.map((a) => (
          <ArtistCard key={a.id} artist={a} />
        ))}
      </div>
    </div>
  )
}
