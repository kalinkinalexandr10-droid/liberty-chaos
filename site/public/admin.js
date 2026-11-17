// Админка: вход, статистика скачиваний, загрузка новых версий, правка описаний, удаление версий.
(function () {
  const $ = (id) => document.getElementById(id);
  const fmtSize = (b) => (b >= 1048576 ? (b / 1048576).toFixed(1) + ' МБ' : Math.max(1, Math.round(b / 1024)) + ' КБ');
  const fmtDate = (iso) => new Date(iso).toLocaleDateString('ru-RU');

  function msg(id, text, ok) {
    const m = $(id);
    m.textContent = text;
    m.className = 'msg ' + (ok ? 'ok' : 'err');
  }

  async function api(url, opts) {
    const r = await fetch(url, Object.assign({ credentials: 'same-origin' }, opts || {}));
    let data = {};
    try { data = await r.json(); } catch (e) { /* пустой ответ */ }
    if (!r.ok) throw new Error(data.error || ('Ошибка ' + r.status));
    return data;
  }

  function el(tag, attrs, children) {
    const e = document.createElement(tag);
    for (const k in attrs || {}) {
      if (k === 'text') e.textContent = attrs[k];
      else if (k === 'value') e.value = attrs[k];
      else e.setAttribute(k, attrs[k]);
    }
    (children || []).forEach((c) => c && e.appendChild(c));
    return e;
  }

  async function showDash() {
    $('login').hidden = true;
    $('dash').hidden = false;
    $('logout').hidden = false;
    const mods = await api('/api/admin/mods');

    // статистика
    const stats = $('stats');
    stats.textContent = '';
    mods.forEach((m) => {
      const total = m.versions.reduce((s, v) => s + v.downloads, 0);
      stats.appendChild(el('tr', {}, [
        el('td', { text: m.name }),
        el('td', { text: String(m.versions.length) }),
        el('td', { text: m.versions[0] ? 'v' + m.versions[0].version : '—' }),
        el('td', { text: String(total) }),
      ]));
    });

    // список модов для загрузки
    const sel = $('up-mod');
    const keep = sel.value;
    sel.textContent = '';
    mods.forEach((m) => sel.appendChild(el('option', { value: m.id, text: m.name })));
    if (keep) sel.value = keep;

    // описания и версии
    const box = $('mods-admin');
    box.textContent = '';
    mods.forEach((m) => box.appendChild(modEditor(m)));
  }

  function modEditor(m) {
    const name = el('input', { value: m.name });
    const hotkey = el('input', { value: m.hotkey || '' });
    const summary = el('textarea', { text: m.summary || '' });
    const status = el('p', { class: 'msg', role: 'status' });
    const save = el('button', { class: 'btn', type: 'button', text: 'Сохранить описание' });
    save.addEventListener('click', async () => {
      try {
        await api('/api/admin/mods/' + encodeURIComponent(m.id), {
          method: 'PATCH', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name: name.value, hotkey: hotkey.value, summary: summary.value }),
        });
        status.textContent = 'Сохранено'; status.className = 'msg ok';
      } catch (e) { status.textContent = e.message; status.className = 'msg err'; }
    });

    const rows = el('tbody');
    m.versions.forEach((v) => {
      const del = el('button', { class: 'btn danger', type: 'button', text: 'Удалить' });
      const confirmBox = el('span', { hidden: '' });
      const yes = el('button', { class: 'btn danger', type: 'button', text: 'Да, удалить v' + v.version });
      const no = el('button', { class: 'btn', type: 'button', text: 'Отмена' });
      confirmBox.append(yes, document.createTextNode(' '), no);
      del.addEventListener('click', () => { del.hidden = true; confirmBox.hidden = false; });
      no.addEventListener('click', () => { del.hidden = false; confirmBox.hidden = true; });
      yes.addEventListener('click', async () => {
        try {
          await api('/api/admin/mods/' + encodeURIComponent(m.id) + '/versions/' + encodeURIComponent(v.version), { method: 'DELETE' });
          showDash();
        } catch (e) { status.textContent = e.message; status.className = 'msg err'; }
      });
      rows.appendChild(el('tr', {}, [
        el('td', { text: 'v' + v.version }),
        el('td', { text: fmtDate(v.date) }),
        el('td', { text: fmtSize(v.size) }),
        el('td', { text: String(v.downloads) }),
        el('td', { text: v.changelog || '' }),
        el('td', {}, [del, confirmBox]),
      ]));
    });
    const table = m.versions.length
      ? el('div', { class: 'table-wrap' }, [el('table', {}, [
          el('thead', {}, [el('tr', {}, ['Версия', 'Дата', 'Размер', 'Скачиваний', 'Изменения', ''].map((t) => el('th', { text: t })))]),
          rows])])
      : el('p', { class: 'empty', text: 'Версий пока нет — загрузи первую формой выше.' });

    return el('section', { style: 'display:grid;gap:10px;border-top:1px solid var(--line);padding-top:14px' }, [
      el('h3', { text: m.name + '  (' + m.id + ')', style: 'margin:0' }),
      el('div', { class: 'form-row' }, [el('label', { text: 'Название' }, [name]), el('label', { text: 'Клавиша' }, [hotkey])]),
      el('label', { text: 'Описание' }, [summary]),
      el('div', { class: 'row' }, [save]),
      status,
      table,
    ]);
  }

  $('login').addEventListener('submit', async (e) => {
    e.preventDefault();
    try {
      await api('/api/admin/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: $('password').value }),
      });
      $('password').value = '';
      showDash();
    } catch (err) { msg('login-msg', err.message, false); }
  });

  $('logout').addEventListener('click', async (e) => {
    e.preventDefault();
    await api('/api/admin/logout', { method: 'POST' }).catch(() => {});
    location.reload();
  });

  $('upload').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData();
    fd.append('version', $('up-version').value.trim());
    fd.append('changelog', $('up-changelog').value);
    fd.append('file', $('up-file').files[0]);
    $('up-btn').disabled = true;
    msg('up-msg', 'Загружаю…', true);
    try {
      await api('/api/admin/mods/' + encodeURIComponent($('up-mod').value) + '/versions', { method: 'POST', body: fd });
      msg('up-msg', 'Версия загружена и уже доступна на сайте', true);
      $('upload').reset();
      showDash();
    } catch (err) { msg('up-msg', err.message, false); }
    $('up-btn').disabled = false;
  });

  api('/api/admin/me')
    .then((r) => { if (r.admin) showDash(); else $('login').hidden = false; })
    .catch(() => { $('login').hidden = false; });
})();
