// Простое хранилище в JSON-файле: каталог модов, версии и счётчики скачиваний.
// Запись атомарная (через временный файл), чтобы при сбое data/db.json не испортился.
const fs = require('fs');
const path = require('path');

const DATA_DIR = path.join(__dirname, '..', 'data');
const DB_FILE = path.join(DATA_DIR, 'db.json');

// Каталог по умолчанию — моды сборки. Описания можно поменять в админке.
const DEFAULT_MODS = [
  {
    id: 'full-pack', name: 'Liberty Chaos Pack — всё сразу', hotkey: 'F5–F9',
    summary: 'Все пять модов одним архивом: транспорт, оружие, NPC, полёт и настройки.',
    files: ['all'],
  },
  {
    id: 'car-spawner', name: 'Транспорт', hotkey: 'F5',
    summary: 'Меню всех 124 моделей транспорта GTA IV с фото: машины, мотоциклы, вертолёты, лодки. Случайная машина на R.',
    files: ['CarSpawner.net.cs', 'CarSpawner'],
  },
  {
    id: 'weapon-menu', name: 'Арсенал', hotkey: 'F6',
    summary: 'Любое оружие игры с максимумом патронов и картинкой, броня и здоровье, «всё сразу» одной кнопкой.',
    files: ['WeaponMenu.net.cs', 'WeaponMenu'],
  },
  {
    id: 'npc-spawner', name: 'Армия NPC', hotkey: 'F7',
    summary: 'Призыв любого из 434 NPC с живым превью. Режимы: драка, охрана (едут за тобой и бьют копов), охота на тебя.',
    files: ['NpcSpawner.net.cs', 'NpcSpawner'],
  },
  {
    id: 'fly-mode', name: 'Полёт', hotkey: 'F8',
    summary: 'Полёт над Либерти-Сити и сброс бомб, машин и людей с высоты. Бессмертие в воздухе.',
    files: ['FlyMode.net.cs'],
  },
  {
    id: 'mod-settings', name: 'Mod menu settings', hotkey: 'F9',
    summary: 'Окно настроек всех модов: язык RU/EN, лимиты машин и NPC, переназначение клавиш.',
    files: ['ModSettings.net.cs'],
  },
];

function emptyDb() {
  return {
    mods: DEFAULT_MODS.map((m) => ({
      id: m.id, name: m.name, hotkey: m.hotkey, summary: m.summary, versions: [],
    })),
  };
}

let cache = null;

function load() {
  if (cache) return cache;
  try {
    cache = JSON.parse(fs.readFileSync(DB_FILE, 'utf8'));
  } catch (e) {
    cache = emptyDb();
    save();
  }
  // новые моды из каталога, которых ещё нет в базе
  for (const m of DEFAULT_MODS) {
    if (!cache.mods.find((x) => x.id === m.id)) {
      cache.mods.push({ id: m.id, name: m.name, hotkey: m.hotkey, summary: m.summary, versions: [] });
    }
  }
  return cache;
}

function save() {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = DB_FILE + '.tmp';
  fs.writeFileSync(tmp, JSON.stringify(cache, null, 2), 'utf8');
  fs.renameSync(tmp, DB_FILE);
}

function getMod(id) {
  return load().mods.find((m) => m.id === id) || null;
}

// версии храним от новой к старой
function latest(mod) {
  return mod.versions.length ? mod.versions[0] : null;
}

module.exports = { load, save, getMod, latest, DEFAULT_MODS, DATA_DIR };
