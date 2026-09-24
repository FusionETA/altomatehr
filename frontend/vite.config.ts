import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

// One id per build, baked into the bundle AND written to /version.json, so a
// tab left open across a deploy can tell it is running an old build. Without
// this an employee who keeps the portal open all day carries on with
// yesterday's code until they happen to reload — which is how a fix can be
// live on the server and still missing on their screen.
const BUILD_ID = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`

function versionFile(): Plugin {
  return {
    name: 'altomatehr-version-file',
    apply: 'build',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'version.json',
        source: JSON.stringify({ build: BUILD_ID }),
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), versionFile()],
  define: {
    'import.meta.env.VITE_BUILD_ID': JSON.stringify(BUILD_ID),
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  // Mirror the production nginx site so the SAME-ORIGIN "/api" prefix that
  // api-client.ts defaults to also works under `npm run dev`. Two details have
  // to match nginx or dev diverges from prod:
  //   • rewrite strips "/api" — backend routes are UNPREFIXED ([Route("employees")]),
  //     nginx does this via `proxy_pass http://127.0.0.1:8080/` with a trailing slash.
  //   • cookiePathRewrite — AuthController sets the refresh cookie Path=/auth,
  //     but the browser sees it under /api/auth; without the rewrite the cookie
  //     is never sent back and session restore silently fails.
  // Going through the proxy also makes the browser see one origin, so the
  // backend CORS policy stops mattering in dev.
  server: {
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:5001',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/api/, ''),
        cookiePathRewrite: { '/auth': '/api/auth' },
      },
    },
  },
})
