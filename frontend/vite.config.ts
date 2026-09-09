import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
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
