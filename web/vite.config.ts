import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In dev the API runs separately (`dotnet run --project src/Ws.Api`, http://localhost:5154).
// In production the API serves the built files from its wwwroot.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': process.env.API_URL ?? 'http://localhost:5154',
    },
  },
})
