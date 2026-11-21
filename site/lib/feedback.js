// Обратная связь: сообщения с сайта хранятся в data/feedback.json и видны только в админке.
// Запись атомарная, как у каталога. Храним не больше MAX_MESSAGES — самые старые удаляются.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { DATA_DIR } = require('./store');

const FILE = path.join(DATA_DIR, 'feedback.json');
const MAX_MESSAGES = 500;
const TOPICS = ['bug', 'idea', 'question', 'other'];

let cache = null;

function load() {
  if (cache) return cache;
  try {
    cache = JSON.parse(fs.readFileSync(FILE, 'utf8'));
    if (!Array.isArray(cache)) cache = [];
  } catch (e) {
    cache = [];
  }
  return cache;
}

function save() {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = FILE + '.tmp';
  fs.writeFileSync(tmp, JSON.stringify(cache, null, 2), 'utf8');
  fs.renameSync(tmp, FILE);
}

const clean = (v, max) => String(v == null ? '' : v).replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, '').trim().slice(0, max);

// Проверяет и сохраняет сообщение. Возвращает { error } или { ok: true }.
function add(body) {
  const b = body || {};
  const message = clean(b.message, 2000);
  if (message.length < 5) return { error: 'Напиши сообщение хотя бы из пары слов' };
  const topic = TOPICS.includes(b.topic) ? b.topic : 'other';
  const list = load();
  list.unshift({
    id: crypto.randomBytes(8).toString('hex'),
    date: new Date().toISOString(),
    topic,
    name: clean(b.name, 60),
    contact: clean(b.contact, 120),
    message,
    read: false,
  });
  if (list.length > MAX_MESSAGES) list.length = MAX_MESSAGES;
  save();
  return { ok: true };
}

function all() { return load(); }

function unreadCount() { return load().filter((m) => !m.read).length; }

function setRead(id, read) {
  const m = load().find((x) => x.id === id);
  if (!m) return false;
  m.read = !!read;
  save();
  return true;
}

function remove(id) {
  const list = load();
  const i = list.findIndex((x) => x.id === id);
  if (i < 0) return false;
  list.splice(i, 1);
  save();
  return true;
}

// защита от спама: не больше 3 сообщений за 10 минут с одного адреса
const recent = new Map();
function sendAllowed(ip) {
  const now = Date.now();
  const listTimes = (recent.get(ip) || []).filter((t) => now - t < 10 * 60 * 1000);
  if (listTimes.length >= 3) { recent.set(ip, listTimes); return false; }
  listTimes.push(now);
  recent.set(ip, listTimes);
  // чистим старые адреса, чтобы карта не росла бесконечно
  if (recent.size > 5000) for (const [k, v] of recent) if (!v.some((t) => now - t < 10 * 60 * 1000)) recent.delete(k);
  return true;
}

module.exports = { add, all, unreadCount, setRead, remove, sendAllowed, TOPICS };
