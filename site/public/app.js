// Главная: список модов из /api/mods, кнопки скачивания, история версий.
(function () {
  const box = document.getElementById('mods');

  const fmtSize = (b) => (b >= 1048576 ? (b / 1048576).toFixed(1) + ' МБ' : Math.max(1, Math.round(b / 1024)) + ' КБ');
  const fmtDate = (iso) => new Date(iso).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
  const plural = (n, one, few, many) => {
    const m10 = n % 10, m100 = n % 100;
    if (m10 === 1 && m100 !== 11) return one;
    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
    return many;
  };

  function el(tag, attrs, children) {
    const e = document.createElement(tag);
    for (const k in attrs || {}) {
      if (k === 'text') e.textContent = attrs[k];
      else e.setAttribute(k, attrs[k]);
    }
    (children || []).forEach((c) => c && e.appendChild(c));
    return e;
  }

  // Анимации внутри кнопки «Скачать» — своя у каждого мода (CSS: .fx-*).
  const FX = {
    'car-spawner':
      '<svg class="fx fx-car" viewBox="0 0 56 28" aria-hidden="true">' +
        '<path class="fx-beam" d="M52 13 L80 6 L80 22 L52 17 Z"/>' +
        '<g class="fx-body"><path class="fx-paint" d="M3 19 L5 13 Q6 11 9 11 L17 10 L24 5 Q25 4 27 4 L38 4 Q40 4 41 5 L47 10 L51 11 Q54 12 54 15 L54 19 Z"/>' +
        '<path class="fx-glass" d="M26 6.5 L38 6.5 L44 10.5 L21 10.5 Z"/><rect class="fx-lamp" x="51" y="12.5" width="3" height="2" rx="1"/></g>' +
        '<g class="fx-wheel" style="transform-origin:14px 20px"><circle cx="14" cy="20" r="4.6"/><path d="M14 16v8M10 20h8"/></g>' +
        '<g class="fx-wheel" style="transform-origin:43px 20px"><circle cx="43" cy="20" r="4.6"/><path d="M43 16v8M39 20h8"/></g>' +
      '</svg>',
    'weapon-menu':
      '<svg class="fx fx-gun" viewBox="0 0 56 28" aria-hidden="true">' +
        '<circle class="fx-flash" cx="28" cy="14" r="9"/>' +
        '<g class="fx-cross"><circle cx="28" cy="14" r="9"/><path d="M28 1v7M28 20v7M15 14h7M34 14h7"/><circle class="fx-dot" cx="28" cy="14" r="1.6"/></g>' +
      '</svg>',
    'npc-spawner':
      '<svg class="fx fx-npc" viewBox="0 0 56 28" aria-hidden="true">' +
        '<g class="fx-guy" style="--i:0"><circle cx="13" cy="9" r="4"/><path d="M6 28v-6a7 7 0 0 1 14 0v6z"/></g>' +
        '<g class="fx-guy" style="--i:1"><circle cx="28" cy="8" r="4.4"/><path d="M20.5 28v-6.5a7.5 7.5 0 0 1 15 0V28z"/></g>' +
        '<g class="fx-guy" style="--i:2"><circle cx="43" cy="9" r="4"/><path d="M36 28v-6a7 7 0 0 1 14 0v6z"/></g>' +
      '</svg>',
    'fly-mode':
      '<svg class="fx fx-fly" viewBox="0 0 56 28" aria-hidden="true">' +
        '<circle class="fx-boom" cx="40" cy="25" r="6"/>' +
        '<circle class="fx-bomb" cx="22" cy="12" r="2.2"/>' +
        '<g class="fx-hero"><circle cx="20" cy="4" r="2.6"/><path d="M14 9 L26 9 L23 12 L23 17 L17 17 L17 12 Z"/><path class="fx-trail" d="M18 18 L16 26 M22 18 L24 26"/></g>' +
      '</svg>',
    'mod-settings':
      '<svg class="fx fx-gear" viewBox="0 0 56 28" aria-hidden="true">' +
        '<g class="fx-g1" style="transform-origin:22px 14px"><path d="M22 4l2 3 3.5-.6.6 3.5 3 2-2 3 2 3-3 2-.6 3.5-3.5-.6-2 3-2-3-3.5.6-.6-3.5-3-2 2-3-2-3 3-2 .6-3.5 3.5.6z"/><circle class="fx-hole" cx="22" cy="14" r="3.4"/></g>' +
        '<g class="fx-g2" style="transform-origin:38px 18px"><path d="M38 11.5l1.4 2 2.4-.4.4 2.4 2 1.4-1.4 2 1.4 2-2 1.4-.4 2.4-2.4-.4-1.4 2-1.4-2-2.4.4-.4-2.4-2-1.4 1.4-2-1.4-2 2-1.4.4-2.4 2.4.4z"/><circle class="fx-hole" cx="38" cy="18" r="2.2"/></g>' +
      '</svg>',
    'extras-menu':
      '<svg class="fx fx-bird" viewBox="0 0 56 28" aria-hidden="true">' +
        '<g class="fx-pigeon"><ellipse cx="28" cy="16" rx="8" ry="5"/><circle cx="36" cy="12" r="3.4"/><path d="M39 12 L42.5 13 L39 14 Z" class="fx-beak"/><path d="M20 16 L14 13 L15 18 Z"/>' +
        '<path class="fx-wing" d="M25 14 Q28 3 34 6 Q30 11 30 15 Z"/><circle class="fx-glow" cx="28" cy="15" r="11"/></g>' +
      '</svg>',
    'full-pack':
      '<svg class="fx fx-pack" viewBox="0 0 56 28" aria-hidden="true">' +
        ['F5', 'F7', 'F10'].map((k, i) =>
          '<g class="fx-key" style="--i:' + i + '"><rect x="' + (2 + i * 18) + '" y="5" width="16" height="17" rx="3"/>' +
          '<text x="' + (10 + i * 18) + '" y="17.5" text-anchor="middle">' + k + '</text></g>').join('') +
      '</svg>',
  };

  function downloadButton(m) {
    const a = el('a', { class: 'btn dl' + (m.id === 'full-pack' ? ' primary' : ''), href: '/api/download/' + encodeURIComponent(m.id) });
    a.appendChild(el('span', { class: 'dl-label', text: 'Скачать' }));
    if (FX[m.id]) {
      const slot = el('span', { class: 'dl-fx', 'aria-hidden': 'true' });
      slot.innerHTML = FX[m.id]; // только наши собственные SVG выше, без данных с сервера
      a.appendChild(slot);
    }
    return a;
  }

  function card(m) {
    const head = el('div', { class: 'mod-head' }, [el('kbd', { text: m.hotkey || '' }), el('h3', { text: m.name })]);
    const meta = el('div', { class: 'meta' });
    if (m.latest) {
      meta.appendChild(el('span', { text: 'v' + m.latest.version }));
      meta.appendChild(el('span', { text: fmtSize(m.latest.size) }));
      meta.appendChild(el('span', { text: fmtDate(m.latest.date) }));
    }
    const dls = el('span');
    dls.appendChild(el('b', { 'data-count': String(m.downloads), text: String(m.downloads) }));
    dls.appendChild(document.createTextNode(' ' + plural(m.downloads, 'скачивание', 'скачивания', 'скачиваний')));
    meta.appendChild(dls);

    const row = el('div', { class: 'row' });
    if (m.latest) row.appendChild(downloadButton(m));
    else row.appendChild(el('span', { class: 'empty', text: 'Скоро' }));

    const parts = [head, el('p', { text: m.summary || '' }), meta, row];

    if (m.history && m.history.length) {
      const list = el('ul');
      m.history.forEach((h) => {
        const li = el('li');
        li.appendChild(el('strong', { text: 'v' + h.version + ' — ' + fmtDate(h.date) + '. ' }));
        li.appendChild(document.createTextNode(h.changelog || 'Без описания.'));
        list.appendChild(li);
      });
      parts.push(el('details', {}, [el('summary', { text: 'Что нового' }), list]));
    }
    return el('article', { class: 'mod mod-' + m.id + (m.id === 'full-pack' ? ' featured' : '') }, parts);
  }

  fetch('/api/mods')
    .then((r) => r.json())
    .then((mods) => {
      box.textContent = '';
      mods.forEach((m) => box.appendChild(card(m)));
      if (window.LCfx) window.LCfx.observe(box.children);
      const full = mods.find((m) => m.id === 'full-pack');
      if (!full || !full.latest) document.getElementById('hero-download').setAttribute('href', '#mods-title');
    })
    .catch(() => { box.innerHTML = '<p class="empty">Не удалось загрузить список модов. Обнови страницу.</p>'; });
})();
