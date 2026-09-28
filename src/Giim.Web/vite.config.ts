import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development, API calls are proxied to Giim.Api (see its launchSettings.json).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
    },
  },
})
