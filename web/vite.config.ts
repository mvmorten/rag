import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server proxies /api to the ASP.NET Core API so the browser never needs CORS.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: process.env.RAG_API_URL ?? 'http://localhost:5062',
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
