// Сайт Liberty Chaos Pack: страницы, скачивание модов со счётчиком и админка версий.
// Запуск: npm install, затем npm start → http://localhost:3000
const express = require('express');
const multer = require('multer');
const fs = require('fs');
const path = require('path');
const store = require('./lib/store');
const auth = require('./lib/auth');

const PORT = Number(process.env.PORT) || 3000;
const FILES_DIR = path.join(__dirname, 'files');       // загруженные архивы версий
const PACKAGES_DIR = path.join(__dirname, 'packages'); // архивы, собранные tools/build-packages.ps1
const MAX_UPLOAD_MB = 300;

fs.mkdirSync(FILES_DIR, { recursive: true });

const app = express();
app.disable('x-powered-by');
// на хостинге (Render) сайт стоит за прокси: так req.ip — настоящий адрес посетителя (для защиты от подбора пароля)
app.set('trust proxy', 1);
const IS_HTTPS_HOST = !!process.env.RENDER || process.env.NODE_ENV === 'production';
app.use(express.json({ limit: '64kb' }));

// базовые заголовки безопасности
app.use((req, res, next) => {
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'same-origin');
  next();
});

const VERSION_RE = /^\d+\.\d+(\.\d+)?$/;
const safeName = (s) => String(s).replace(/[^a-zA-Z0-9._-]/g, '_');

function publicMod(mod) {
  const v = store.latest(mod);
  return {
    id: mod.id,
    name: mod.name,
    hotkey: mod.hotkey,
    summary: mod.summary,
    downloads: mod.versions.reduce((s, x) => s + (x.downloads || 0), 0),
    latest: v ? { version: v.version, date: v.date, size: v.size, changelog: v.changelog } : null,
    history: mod.versions.map((x) => ({ version: x.version, date: x.date, changelog: x.changelog })),
  };
}

// ---------- публичное API ----------
app.get('/api/mods', (req, res) => {
  res.json(store.load().mods.map(publicMod));
});

app.get('/api/download/:id', (req, res) => {
  const mod = store.getMod(req.params.id);
  if (!mod) return res.status(404).send('Мод не найден');
  const v = req.query.version ? mod.versions.find((x) => x.version === req.query.version) : store.latest(mod);
  if (!v) return res.status(404).send('У этого мода пока нет загруженных версий');
  const file = path.join(FILES_DIR, v.file);
  if (!file.startsWith(FILES_DIR) || !fs.existsSync(file)) return res.status(410).send('Файл версии не найден на сервере');
  // считаем только настоящие скачивания: HEAD-запросы проверялок ссылок файл не получают
  if (req.method === 'GET') {
    v.downloads = (v.downloads || 0) + 1;
    store.save();
  }
  res.download(file, `LibertyChaos-${mod.id}-${v.version}.zip`);
});

// ---------- админка ----------
app.post('/api/admin/login', (req, res) => {
  const ip = req.ip || 'local';
  if (!auth.loginAllowed(ip)) return res.status(429).json({ error: 'Слишком много попыток. Подожди минуту.' });
  if (!auth.checkPassword(req.body && req.body.password)) return res.status(401).json({ error: 'Неверный пароль' });
  const token = auth.createSession();
  res.cookie('lc_admin', token, { httpOnly: true, sameSite: 'strict', secure: IS_HTTPS_HOST, maxAge: auth.SESSION_TTL_MS, path: '/' });
  res.json({ ok: true });
});

app.post('/api/admin/logout', (req, res) => {
  auth.destroySession(req);
  res.clearCookie('lc_admin', { path: '/' });
  res.json({ ok: true });
});

app.get('/api/admin/me', (req, res) => res.json({ admin: auth.isAdmin(req) }));

app.get('/api/admin/mods', auth.requireAdmin, (req, res) => {
  res.json(store.load().mods.map((m) => ({
    id: m.id, name: m.name, hotkey: m.hotkey, summary: m.summary,
    versions: m.versions.map((v) => ({ version: v.version, date: v.date, size: v.size, downloads: v.downloads || 0, changelog: v.changelog })),
  })));
});

app.patch('/api/admin/mods/:id', auth.requireAdmin, (req, res) => {
  const mod = store.getMod(req.params.id);
  if (!mod) return res.status(404).json({ error: 'Мод не найден' });
  const b = req.body || {};
  if (typeof b.name === 'string' && b.name.trim()) mod.name = b.name.trim().slice(0, 80);
  if (typeof b.hotkey === 'string') mod.hotkey = b.hotkey.trim().slice(0, 20);
  if (typeof b.summary === 'string') mod.summary = b.summary.trim().slice(0, 600);
  store.save();
  res.json({ ok: true });
});

const upload = multer({
  dest: path.join(FILES_DIR, '.incoming'),
  limits: { fileSize: MAX_UPLOAD_MB * 1024 * 1024, files: 1 },
  fileFilter: (req, file, cb) => cb(null, /\.zip$/i.test(file.originalname)),
});

app.post('/api/admin/mods/:id/versions', auth.requireAdmin, upload.single('file'), (req, res) => {
  const cleanup = () => { if (req.file) fs.rm(req.file.path, { force: true }, () => {}); };
  const mod = store.getMod(req.params.id);
  if (!mod) { cleanup(); return res.status(404).json({ error: 'Мод не найден' }); }
  if (!req.file) return res.status(400).json({ error: 'Прикрепи .zip-архив версии' });
  const version = String((req.body && req.body.version) || '').trim();
  if (!VERSION_RE.test(version)) { cleanup(); return res.status(400).json({ error: 'Версия должна быть вида 1.2 или 1.2.3' }); }
  if (mod.versions.find((v) => v.version === version)) { cleanup(); return res.status(409).json({ error: `Версия ${version} уже есть` }); }
  const fileName = `${safeName(mod.id)}-${safeName(version)}.zip`;
  fs.renameSync(req.file.path, path.join(FILES_DIR, fileName));
  mod.versions.unshift({
    version,
    date: new Date().toISOString(),
    size: req.file.size,
    downloads: 0,
    changelog: String((req.body && req.body.changelog) || '').trim().slice(0, 4000),
    file: fileName,
  });
  store.save();
  res.json({ ok: true });
});

app.delete('/api/admin/mods/:id/versions/:version', auth.requireAdmin, (req, res) => {
  const mod = store.getMod(req.params.id);
  if (!mod) return res.status(404).json({ error: 'Мод не найден' });
  const i = mod.versions.findIndex((v) => v.version === req.params.version);
  if (i < 0) return res.status(404).json({ error: 'Версия не найдена' });
  const [v] = mod.versions.splice(i, 1);
  fs.rm(path.join(FILES_DIR, v.file), { force: true }, () => {});
  store.save();
  res.json({ ok: true });
});

// ошибки загрузки (слишком большой файл и т.п.)
app.use((err, req, res, next) => {
  if (err instanceof multer.MulterError) {
    return res.status(400).json({ error: err.code === 'LIMIT_FILE_SIZE' ? `Файл больше ${MAX_UPLOAD_MB} МБ` : err.message });
  }
  console.error(err);
  res.status(500).json({ error: 'Ошибка сервера' });
});

// ---------- страницы ----------
app.use(express.static(path.join(__dirname, 'public'), { extensions: ['html'] }));

// ---------- первый запуск ----------
// Если у мода ещё нет версий, а в packages/ лежит собранный архив — регистрируем его как 1.0.0.
function seedFromPackages() {
  if (!fs.existsSync(PACKAGES_DIR)) return 0;
  let n = 0;
  for (const mod of store.load().mods) {
    if (mod.versions.length) continue;
    const src = path.join(PACKAGES_DIR, `${mod.id}.zip`);
    if (!fs.existsSync(src)) continue;
    const fileName = `${mod.id}-1.0.0.zip`;
    fs.copyFileSync(src, path.join(FILES_DIR, fileName));
    mod.versions.push({
      version: '1.0.0', date: new Date().toISOString(), size: fs.statSync(src).size, downloads: 0,
      changelog: 'Первая версия.', file: fileName,
    });
    n++;
  }
  if (n) store.save();
  return n;
}

// На хостинге пароль задаётся переменной окружения ADMIN_PASSWORD (в панели Render):
// диск там временный, и сгенерированный пароль пропадал бы при каждом перезапуске.
let newPass = null;
if (process.env.ADMIN_PASSWORD) auth.setPassword(process.env.ADMIN_PASSWORD);
else newPass = auth.ensurePassword();
const seeded = seedFromPackages();

app.listen(PORT, () => {
  console.log(`Liberty Chaos site: http://localhost:${PORT}`);
  console.log(`Админка: http://localhost:${PORT}/admin`);
  if (seeded) console.log(`Добавлено архивов из packages/: ${seeded}`);
  if (newPass) {
    console.log('');
    console.log('  Пароль админки (показывается ОДИН раз, запиши его): ' + newPass);
    console.log('  Сменить: npm run set-password -- НовыйПароль');
    console.log('');
  }
});
