import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { installNumberInputGuard } from './shared/lib/number-input-guard'

// Before the first render: a wheel tick over a focused number input must never
// be read as an edit.
installNumberInputGuard()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
