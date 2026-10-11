// E2E 用靜態伺服器：提供前端 production 建置結果（src/frontend/dist/myerp-frontend/browser）。
// - 存在的檔案直接回傳；
// - 有副檔名但不存在的路徑、/api/ 開頭的路徑回 404（不要用 index.html 掩蓋缺檔）；
// - 其他路徑回 index.html（Angular 路由）；
// - 拒絕跳出根目錄的路徑。
// - 指定 --api（或環境變數 E2E_API_URL）時，/api/ 開頭的請求轉送到該後端。
// 用法：node serve-dist.mjs [--port 4300] [--root <dir>] [--api http://127.0.0.1:5180]
import { createReadStream, statSync } from 'node:fs';
import { createServer, request as httpRequest } from 'node:http';
import { extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));

function argValue(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

const port = Number(argValue('--port', process.env.E2E_PORT ?? '4300'));
const root = resolve(argValue('--root', join(here, '..', '..', 'src', 'frontend', 'dist', 'myerp-frontend', 'browser')));
const apiOrigin = argValue('--api', process.env.E2E_API_URL ?? '') || null;

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.ico': 'image/x-icon',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

function isFile(path) {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
}

// 與正式前端站台建議的安全標頭相同（docs/01-architecture/security.md 第 5 節），E2E 因此驗證前端在此 CSP 下可正常運作。
// connect-src 只有 'self'：E2E 的 API 以 page.route 模擬同源請求；正式部署 API 不同源時須加入 API 來源。
const SECURITY_HEADERS = {
  'Content-Security-Policy': [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data:",
    "font-src 'self' data:",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join('; '),
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
};

function send(res, status, filePath) {
  res.writeHead(status, {
    ...SECURITY_HEADERS,
    'Content-Type': CONTENT_TYPES[extname(filePath).toLowerCase()] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
  });
  createReadStream(filePath).pipe(res);
}

function notFound(res) {
  res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end('Not Found');
}

if (!isFile(join(root, 'index.html'))) {
  console.error(`找不到 ${join(root, 'index.html')}，請先建置前端（npx ng build）。`);
  process.exit(1);
}

// --api 指定時，把 /api/ 請求轉送到真實後端（同源，CSP connect-src 'self' 不變；WU-SYS-013 真實 API E2E）
function proxyToApi(req, res) {
  const target = new URL(req.url ?? '/', apiOrigin);
  const upstream = httpRequest(
    target,
    { method: req.method, headers: { ...req.headers, host: target.host } },
    (apiRes) => {
      res.writeHead(apiRes.statusCode ?? 502, apiRes.headers);
      apiRes.pipe(res);
    },
  );
  upstream.on('error', () => {
    if (!res.headersSent) {
      res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
    }
    res.end('Bad Gateway');
  });
  req.pipe(upstream);
}

const server = createServer((req, res) => {
  if (apiOrigin && (req.url ?? '').startsWith('/api/')) {
    proxyToApi(req, res);
    return;
  }
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.writeHead(405, { Allow: 'GET, HEAD' });
    res.end();
    return;
  }

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(req.url ?? '/', 'http://localhost').pathname);
  } catch {
    res.writeHead(400);
    res.end();
    return;
  }

  const filePath = resolve(root, '.' + pathname);
  if (filePath !== root && !filePath.startsWith(root + sep)) {
    notFound(res);
    return;
  }
  if (isFile(filePath)) {
    send(res, 200, filePath);
    return;
  }
  if (pathname.startsWith('/api/') || extname(pathname) !== '') {
    notFound(res);
    return;
  }
  send(res, 200, join(root, 'index.html'));
});

server.listen(port, '127.0.0.1', () => {
  console.log(`MyERP E2E 靜態伺服器：http://127.0.0.1:${port}/（${root}）`);
});
