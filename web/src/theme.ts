/**
 * Visual themes defined in index.css. Switch with ?theme=bark|terracotta|shoji|moss (remembered in this browser).
 * "shoji" is the base :root palette (no data-theme attribute); the others override it.
 */
const THEMES = ['bark', 'terracotta', 'shoji', 'moss'] as const
type Theme = (typeof THEMES)[number]
const DEFAULT: Theme = 'moss'

const KEY = 'ws-theme'

const META_COLOR: Record<Theme, string> = { bark: '#f3e9dc', terracotta: '#e5d9c5', shoji: '#f4f0e8', moss: '#2f3a2c' }

export function applyTheme() {
  let theme: Theme = DEFAULT
  const fromUrl = new URLSearchParams(location.search).get('theme')
  try {
    if (THEMES.includes(fromUrl as Theme)) localStorage.setItem(KEY, fromUrl!)
    const saved = localStorage.getItem(KEY)
    if (THEMES.includes(saved as Theme)) theme = saved as Theme
  } catch {
    if (THEMES.includes(fromUrl as Theme)) theme = fromUrl as Theme
  }
  if (theme === 'shoji') delete document.documentElement.dataset.theme
  else document.documentElement.dataset.theme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', META_COLOR[theme])
}
