import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/v1': 'http://localhost:5099', '/health': 'http://localhost:5099' } },   // Dev-Proxy; Prod: Caddy
  build: { outDir: 'dist', sourcemap: false }
});
