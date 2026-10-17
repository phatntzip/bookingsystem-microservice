import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';

// Dev proxy so the browser talks same-origin (no CORS on the backend).
//   /ai/*  -> AI Orchestration  (chat + confirm)
//   /gw/*  -> API Gateway        (catalog + booking list endpoints)
// Override targets with VITE_AI_TARGET / VITE_GW_TARGET (e.g. the Aspire-assigned ports).
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const aiTarget = env.VITE_AI_TARGET || 'http://localhost:5000';
  const gwTarget = env.VITE_GW_TARGET || 'http://localhost:64963';

  return {
    plugins: [vue()],
    server: {
      port: 5173,
      proxy: {
        '/ai': { target: aiTarget, changeOrigin: true, rewrite: (p) => p.replace(/^\/ai/, '') },
        '/gw': { target: gwTarget, changeOrigin: true, rewrite: (p) => p.replace(/^\/gw/, '') },
      },
    },
  };
});
