// ============================================================================
// TaoAdmin 前端静态服务 + /api 反向代理（原生路线专用，无需 Nginx/Docker）
// 职责：托管前端静态文件 + /api 反代到网关(5000) + SPA 路由回退
// 启动：node frontend-server.js   （由 install-windows.ps1 自动拉起）
// 端口：默认 8080，可用环境变量 FRONTEND_PORT 覆盖
// ============================================================================
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number(process.env.FRONTEND_PORT || 8080);
const DIST = process.env.FRONTEND_DIST || path.join(__dirname, '..', '..', 'publish', 'frontend');
const GATEWAY = process.env.GATEWAY_URL || 'http://127.0.0.1:5000';

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.mjs': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.eot': 'application/vnd.ms-fontobject',
  '.map': 'application/json',
  '.txt': 'text/plain; charset=utf-8',
};

function log(msg) {
  console.log('[' + new Date().toISOString() + '] ' + msg);
}

// —— /api 反向代理到网关（与 nginx.conf 的 location /api/ 同口径，不改写路径）——
function proxyApi(req, res) {
  const upstream = new URL(GATEWAY + req.url);
  const options = {
    hostname: upstream.hostname,
    port: upstream.port || 80,
    path: upstream.pathname + upstream.search,
    method: req.method,
    headers: Object.assign({}, req.headers, { host: upstream.host }),
  };
  const preq = http.request(options, (pres) => {
    res.writeHead(pres.statusCode, pres.headers);
    pres.pipe(res);
  });
  preq.on('error', (err) => {
    log('[proxy] /api 转发失败: ' + err.message);
    res.writeHead(502, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ message: '网关暂不可用，请稍后重试' }));
  });
  req.pipe(preq);
}

// —— 静态文件 + SPA 回退 ——
function serveStatic(req, res) {
  let urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
  if (urlPath === '/') urlPath = '/index.html';
  const filePath = path.normalize(path.join(DIST, urlPath));
  // 防目录穿越：解析后必须仍在 DIST 内
  if (!filePath.startsWith(path.normalize(DIST + path.sep)) && filePath !== path.normalize(DIST)) {
    res.writeHead(403); res.end('Forbidden'); return;
  }
  fs.stat(filePath, (err, st) => {
    const target = (!err && st.isFile()) ? filePath : path.join(DIST, 'index.html');
    if (!fs.existsSync(target)) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('前端产物缺失，请先执行安装（install-windows.ps1）');
      return;
    }
    const ext = path.extname(target).toLowerCase();
    res.writeHead(200, {
      'Content-Type': MIME[ext] || 'application/octet-stream',
      'Cache-Control': ext === '.html' ? 'no-cache' : 'public, max-age=604800, immutable',
    });
    fs.createReadStream(target).pipe(res);
  });
}

const server = http.createServer((req, res) => {
  if ((req.url || '').startsWith('/api/')) { proxyApi(req, res); return; }
  serveStatic(req, res);
});

server.listen(PORT, '0.0.0.0', () => {
  log('前端服务已启动: http://localhost:' + PORT + '  (静态目录 ' + DIST + '，/api → ' + GATEWAY + ')');
});

process.on('SIGINT', () => process.exit(0));
process.on('SIGTERM', () => process.exit(0));
