import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  publicDir: fileURLToPath(new URL('../../data/world/v0/tiles/', import.meta.url)),
  server: { port: 5173, strictPort: true, proxy: {'/ws': {target: 'ws://127.0.0.1:8080', ws: true}} },
  build: { target: 'es2022' },
});
