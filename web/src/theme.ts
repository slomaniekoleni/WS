/** Visual themes defined in index.css. Switch with ?theme=moss (remembered in this browser). */
const THEMES = ['shoji', 'moss'] as const
type Theme = (typeof THEMES)[number]
const DEFAULT: Theme = 'shoji'

const META_COLOR: Record<Theme, string> = { shoji: '#f4f0e8', moss: '#2f3a2c' }

export function applyTheme() {
  let theme: Theme = DEFAULT
  const fromUrl = new URLSearchParams(location.search).get('theme')
  try {
    if (THEMES.includes(fromUrl as Theme)) localStorage.setItem('theme', fromUrl!)
    const saved = localStorage.getItem('theme')
    if (THEMES.includes(saved as Theme)) theme = saved as Theme
  } catch {
    if (THEMES.includes(fromUrl as Theme)) theme = fromUrl as Theme
  }
  if (theme === DEFAULT) delete document.documentElement.dataset.theme
  else document.documentElement.dataset.theme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', META_COLOR[theme])
}
