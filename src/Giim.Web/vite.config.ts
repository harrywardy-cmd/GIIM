import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development, API and sign-in calls are proxied to Giim.Api (see its launchSettings.json).
// changeOrigin: false keeps the browser's host (localhost:5173), so the sign-in cookie, Okta's callback address
// and QR links all use the address you're actually browsing.
const api = { target: 'http://localhost:5080', changeOrigin: false }

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': api,
      '/health': api,
      '/auth': api,
      '/signin-oidc': api,
      '/signout-callback-oidc': api,
    },
  },
})
