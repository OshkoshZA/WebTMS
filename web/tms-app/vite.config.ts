import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  server: {
    proxy: {
      // Matches the real API base path exactly (api/client.ts's own BASE) — a plain
      // '/api' prefix would also swallow any future frontend route that happens to
      // start with those same four characters (e.g. this app's own /api-clients
      // screens), forwarding it to the backend instead of the SPA and 404ing.
      '/api/v1': {
        target: 'http://localhost:5120',
        changeOrigin: true,
      },
    },
  },
})
