import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { installNumberInputGuard } from './shared/lib/number-input-guard'
import { registerServiceWorker } from './shared/lib/register-sw'

// Before the first render: a wheel tick over a focused number input must never
// be read as an edit.
installNumberInputGuard()
registerServiceWorker()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
