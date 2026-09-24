import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

export default defineConfig({
  plugins: [react()],
  base: '/device-admin-react-preview/',
  build: {
    outDir: path.resolve(__dirname, '../MiLife.DeviceIntake.Api/wwwroot/device-admin-react-preview'),
    emptyOutDir: true,
    sourcemap: false
  }
});
