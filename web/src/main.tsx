import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App'
import { DataProvider } from './data'
import { I18nProvider } from './i18n'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <I18nProvider>
        <DataProvider>
          <App />
        </DataProvider>
      </I18nProvider>
    </BrowserRouter>
  </StrictMode>,
)
