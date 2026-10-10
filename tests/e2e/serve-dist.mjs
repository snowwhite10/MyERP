// E2E 用靜態伺服器：提供前端 production 建置結果（src/frontend/dist/myerp-frontend/browser）。
// - 存在的檔案直接回傳；
// - 有副檔名但不存在的路徑、/api/ 開頭的路徑回 404（不要用 index.html 掩蓋缺檔）；
// - 其他路徑回 index.html（Angular 路由）；
// - 拒絕跳出根目錄的路徑。
// 用法：node serve-dist.mjs [--port 4300] [--root <dir>]
import { createReadStream, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));

function argValue(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

const port = Number(argValue('--port', process.env.E2E_PORT ?? '4300'));
const root = resolve(argValue('--root', join(here, '..', '..', 'src', 'frontend', 'dist', 'myerp-frontend', 'browser')));

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

function send(res, status, filePath) {
  res.writeHead(status, {
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

const server = createServer((req, res) => {
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
