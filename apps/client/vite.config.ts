import { defineConfig } from 'vite';

export default defineConfig({
  publicDir: '../../data/world/v0/tiles',
  server: { port: 5173, strictPort: true },
  build: { target: 'es2022' },
});
