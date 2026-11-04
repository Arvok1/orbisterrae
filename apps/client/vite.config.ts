import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  publicDir: fileURLToPath(new URL('../../data/world/v0/tiles/', import.meta.url)),
  server: { port: 5173, strictPort: true, proxy: {'/ws': {target: 'ws://127.0.0.1:8080', ws: true},'/api':{target:'http://127.0.0.1:8080'}} },
  build: {
    target: 'es2022',
    rollupOptions: {output: {manualChunks(id) {
      if (id.includes('world-land-v1.json')) return 'world-geography';
      if (id.includes('/node_modules/react/') || id.includes('/node_modules/react-dom/') || id.includes('/node_modules/scheduler/')) return 'react-ui';
    }}},
  },
});
