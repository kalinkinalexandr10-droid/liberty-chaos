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

  function card(m) {
    const head = el('div', { class: 'mod-head' }, [el('kbd', { text: m.hotkey || '' }), el('h3', { text: m.name })]);
    const meta = el('div', { class: 'meta' });
    if (m.latest) {
      meta.appendChild(el('span', { text: 'v' + m.latest.version }));
      meta.appendChild(el('span', { text: fmtSize(m.latest.size) }));
      meta.appendChild(el('span', { text: fmtDate(m.latest.date) }));
    }
    meta.appendChild(el('span', { text: m.downloads + ' ' + plural(m.downloads, 'скачивание', 'скачивания', 'скачиваний') }));

    const row = el('div', { class: 'row' });
    if (m.latest) row.appendChild(el('a', { class: 'btn' + (m.id === 'full-pack' ? ' primary' : ''), href: '/api/download/' + encodeURIComponent(m.id), text: 'Скачать' }));
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
    return el('article', { class: 'mod' + (m.id === 'full-pack' ? ' featured' : '') }, parts);
  }

  fetch('/api/mods')
    .then((r) => r.json())
    .then((mods) => {
      box.textContent = '';
      mods.forEach((m) => box.appendChild(card(m)));
      const full = mods.find((m) => m.id === 'full-pack');
      if (!full || !full.latest) document.getElementById('hero-download').setAttribute('href', '#mods-title');
    })
    .catch(() => { box.innerHTML = '<p class="empty">Не удалось загрузить список модов. Обнови страницу.</p>'; });
})();
