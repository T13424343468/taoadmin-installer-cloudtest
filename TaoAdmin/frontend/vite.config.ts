import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

// ============================================================================
// Vite 构建配置
// 【本地】npm run dev —— 开发服务器带热更新，/api 代理到本地网关
// 【线上】npm run build —— 产物输出到 dist/，由 Nginx 托管（见 nginx.conf），
//         Nginx 同样把 /api 反代到网关，因此业务代码无需区分环境
// ============================================================================
export default defineConfig({
  plugins: [vue()],
  resolve: {
    // @ 指向 src 目录，页面里 import xxx from '@/api/xxx'
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      // 【本地】开发环境代理：所有 /api 请求转发到后端网关
      // 后端网关不在本机时改成对应地址，如 'http://192.168.1.100:5000'
      // 【线上】此配置不生效，走 Nginx 的 /api 反代（见 nginx.conf）
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    // Ant Design Vue 体积较大，放宽分包警告阈值
    chunkSizeWarningLimit: 1500,
  },
});
