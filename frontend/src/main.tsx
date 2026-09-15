import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AppraisifyRelay } from './features/appraisify/components/AppraisifyRelay'
import { installNumberInputGuard } from './shared/lib/number-input-guard'
import { registerServiceWorker } from './shared/lib/register-sw'

// Before the first render: a wheel tick over a focused number input must never
// be read as an edit.
installNumberInputGuard()
registerServiceWorker()

// No router in this app (see App.tsx) — a single manual branch is enough for
// the one other real URL this app needs to respond to, rather than pulling
// in a routing library for it.
const root = window.location.pathname === '/appraisify-relay' ? <AppraisifyRelay /> : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {root}
  </StrictMode>,
)
