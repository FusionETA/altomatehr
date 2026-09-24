import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AppraisifyRelay } from './features/appraisify/components/AppraisifyRelay'
import { installNumberInputGuard } from './shared/lib/number-input-guard'
import { registerServiceWorker } from './shared/lib/register-sw'
import { UpdateAvailableBanner } from './shared/components/UpdateAvailableBanner'
import { apiUrl } from './shared/lib/api-client'

// Before the first render: a wheel tick over a focused number input must never
// be read as an edit.
installNumberInputGuard()
registerServiceWorker()

// SSO hand-off from a partner (New-Altomate). The ticket's redirectPath is
// "/sso/callback?t=…", and partners put it after THIS app's address, as the
// previous system's contract had them do. But the callback is a backend route,
// and here the backend lives under the API prefix ("/api" behind nginx), so
// the app would otherwise serve itself for that URL and the ticket would die
// unused. Forward it — replace(), so the ticket isn't left in history.
const ssoHandoff = window.location.pathname === '/sso/callback'
if (ssoHandoff) window.location.replace(`${apiUrl('/sso/callback')}${window.location.search}`)

// No router in this app (see App.tsx) — a single manual branch is enough for
// the one other real URL this app needs to respond to, rather than pulling
// in a routing library for it.
const root = ssoHandoff ? null :
  window.location.pathname === '/appraisify-relay' ? (
    <AppraisifyRelay />
  ) : (
    <>
      <App />
      {/* Admin and employee portal alike — both are left open across deploys. */}
      <UpdateAvailableBanner />
    </>
  )

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {root}
  </StrictMode>,
)
