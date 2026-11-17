// Вход в админку: пароль хранится только в виде хэша (scrypt) в data/admin.json,
// сессия — случайный токен в HttpOnly-cookie. Никаких внешних библиотек.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { DATA_DIR } = require('./store');

const ADMIN_FILE = path.join(DATA_DIR, 'admin.json');
const SESSION_TTL_MS = 12 * 60 * 60 * 1000; // 12 часов
const sessions = new Map(); // token -> expiresAt

function hashPassword(password, salt) {
  salt = salt || crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return { salt, hash };
}

function setPassword(password) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  fs.writeFileSync(ADMIN_FILE, JSON.stringify(hashPassword(password), null, 2), 'utf8');
}

// При первом запуске пароля нет — генерируем случайный и показываем его в консоли один раз.
function ensurePassword() {
  if (fs.existsSync(ADMIN_FILE)) return null;
  const pass = crypto.randomBytes(9).toString('base64url');
  setPassword(pass);
  return pass;
}

function checkPassword(password) {
  try {
    const { salt, hash } = JSON.parse(fs.readFileSync(ADMIN_FILE, 'utf8'));
    const test = hashPassword(String(password || ''), salt).hash;
    return crypto.timingSafeEqual(Buffer.from(test, 'hex'), Buffer.from(hash, 'hex'));
  } catch (e) {
    return false;
  }
}

function createSession() {
  const token = crypto.randomBytes(32).toString('hex');
  sessions.set(token, Date.now() + SESSION_TTL_MS);
  return token;
}

function readCookie(req, name) {
  const raw = req.headers.cookie || '';
  for (const part of raw.split(';')) {
    const [k, ...v] = part.trim().split('=');
    if (k === name) return decodeURIComponent(v.join('='));
  }
  return null;
}

function isAdmin(req) {
  const token = readCookie(req, 'lc_admin');
  if (!token) return false;
  const exp = sessions.get(token);
  if (!exp || exp < Date.now()) { sessions.delete(token); return false; }
  return true;
}

function destroySession(req) {
  const token = readCookie(req, 'lc_admin');
  if (token) sessions.delete(token);
}

function requireAdmin(req, res, next) {
  if (isAdmin(req)) return next();
  res.status(401).json({ error: 'Нужно войти в админку' });
}

// защита от подбора пароля: не больше 5 попыток в минуту с одного адреса
const attempts = new Map();
function loginAllowed(ip) {
  const now = Date.now();
  const list = (attempts.get(ip) || []).filter((t) => now - t < 60 * 1000);
  if (list.length >= 5) { attempts.set(ip, list); return false; }
  list.push(now);
  attempts.set(ip, list);
  return true;
}

module.exports = {
  ensurePassword, setPassword, checkPassword, createSession, isAdmin, destroySession,
  requireAdmin, loginAllowed, SESSION_TTL_MS,
};
