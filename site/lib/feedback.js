// Обратная связь: сообщения с сайта, видны только в админке.
// Где хранятся:
//   - задана переменная DATABASE_URL (Postgres, например бесплатный Neon) — в таблице feedback.
//     Так сообщения переживают перезапуски сервера на Render, где диск временный;
//   - не задана (локальный запуск) — в data/feedback.json, как раньше.
// Храним не больше MAX_MESSAGES — самые старые удаляются.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { DATA_DIR } = require('./store');

const FILE = path.join(DATA_DIR, 'feedback.json');
const MAX_MESSAGES = 500;
const TOPICS = ['bug', 'idea', 'question', 'other'];

const clean = (v, max) => String(v == null ? '' : v).replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, '').trim().slice(0, max);

// ---------- хранилище в файле ----------
function fileBackend() {
  let cache = null;
  const load = () => {
    if (cache) return cache;
    try { cache = JSON.parse(fs.readFileSync(FILE, 'utf8')); if (!Array.isArray(cache)) cache = []; } catch (e) { cache = []; }
    return cache;
  };
  const save = () => {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const tmp = FILE + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(cache, null, 2), 'utf8');
    fs.renameSync(tmp, FILE);
  };
  return {
    name: 'файл data/feedback.json',
    async init() {},
    async insert(m) {
      const list = load();
      list.unshift(m);
      if (list.length > MAX_MESSAGES) list.length = MAX_MESSAGES;
      save();
    },
    async list() { return load(); },
    async setRead(id, read) {
      const m = load().find((x) => x.id === id);
      if (!m) return false;
      m.read = read; save(); return true;
    },
    async remove(id) {
      const list = load();
      const i = list.findIndex((x) => x.id === id);
      if (i < 0) return false;
      list.splice(i, 1); save(); return true;
    },
  };
}

// ---------- хранилище в Postgres ----------
function pgBackend(url) {
  const { Pool } = require('pg');
  // бесплатная база засыпает без запросов — даём ей время проснуться
  const pool = new Pool({ connectionString: url, max: 3, connectionTimeoutMillis: 15000, idleTimeoutMillis: 30000 });
  pool.on('error', (e) => console.error('Postgres:', e.message));
  const row = (r) => ({
    id: r.id, date: new Date(r.created_at).toISOString(), topic: r.topic,
    name: r.name, contact: r.contact, message: r.message, read: r.read,
  });
  return {
    name: 'база Postgres (DATABASE_URL)',
    async init() {
      await pool.query(`CREATE TABLE IF NOT EXISTS feedback (
        id         text PRIMARY KEY,
        created_at timestamptz NOT NULL DEFAULT now(),
        topic      text NOT NULL,
        name       text NOT NULL DEFAULT '',
        contact    text NOT NULL DEFAULT '',
        message    text NOT NULL,
        read       boolean NOT NULL DEFAULT false
      )`);
    },
    async insert(m) {
      await pool.query(
        'INSERT INTO feedback (id, created_at, topic, name, contact, message, read) VALUES ($1, $2, $3, $4, $5, $6, $7)',
        [m.id, m.date, m.topic, m.name, m.contact, m.message, m.read]);
      await pool.query('DELETE FROM feedback WHERE id IN (SELECT id FROM feedback ORDER BY created_at DESC OFFSET $1)', [MAX_MESSAGES]);
    },
    async list() {
      const r = await pool.query('SELECT * FROM feedback ORDER BY created_at DESC LIMIT $1', [MAX_MESSAGES]);
      return r.rows.map(row);
    },
    async setRead(id, read) {
      const r = await pool.query('UPDATE feedback SET read = $2 WHERE id = $1', [id, read]);
      return r.rowCount > 0;
    },
    async remove(id) {
      const r = await pool.query('DELETE FROM feedback WHERE id = $1', [id]);
      return r.rowCount > 0;
    },
  };
}

const backend = process.env.DATABASE_URL ? pgBackend(process.env.DATABASE_URL) : fileBackend();

// Проверяет и сохраняет сообщение. Возвращает { error } или { ok: true }.
async function add(body) {
  const b = body || {};
  const message = clean(b.message, 2000);
  if (message.length < 5) return { error: 'Напиши сообщение хотя бы из пары слов' };
  await backend.insert({
    id: crypto.randomBytes(8).toString('hex'),
    date: new Date().toISOString(),
    topic: TOPICS.includes(b.topic) ? b.topic : 'other',
    name: clean(b.name, 60),
    contact: clean(b.contact, 120),
    message,
    read: false,
  });
  return { ok: true };
}

const all = () => backend.list();
const setRead = (id, read) => backend.setRead(String(id), !!read);
const remove = (id) => backend.remove(String(id));

// защита от спама: не больше 3 сообщений за 10 минут с одного адреса
const recent = new Map();
function sendAllowed(ip) {
  const now = Date.now();
  const times = (recent.get(ip) || []).filter((t) => now - t < 10 * 60 * 1000);
  if (times.length >= 3) { recent.set(ip, times); return false; }
  times.push(now);
  recent.set(ip, times);
  // чистим старые адреса, чтобы карта не росла бесконечно
  if (recent.size > 5000) for (const [k, v] of recent) if (!v.some((t) => now - t < 10 * 60 * 1000)) recent.delete(k);
  return true;
}

module.exports = { init: () => backend.init(), storageName: backend.name, add, all, setRead, remove, sendAllowed, TOPICS };
