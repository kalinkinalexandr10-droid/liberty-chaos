// Форма обратной связи: отправляет сообщение на /api/feedback.
(function () {
  const $ = (id) => document.getElementById(id);
  const form = $('fb-form');
  const text = $('fb-message');

  const updateCount = () => { $('fb-count').textContent = text.value.length + ' / 2000'; };
  text.addEventListener('input', updateCount);

  // бумажный самолётик вылетает из кнопки «Отправить» (CSS .plane; при «уменьшить движение» не показываем)
  function launchPlane(btn) {
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const r = btn.getBoundingClientRect();
    const p = document.createElement('div');
    p.className = 'plane';
    p.setAttribute('aria-hidden', 'true');
    p.innerHTML = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M2 11.5 22 3l-7.5 19-3.2-7.3z"/><path d="M11.3 14.7 22 3 8 13z" opacity=".6"/></svg>';
    p.style.left = (r.left + r.width / 2 - 17) + 'px';
    p.style.top = (r.top + r.height / 2 - 17) + 'px';
    document.body.appendChild(p);
    setTimeout(() => p.remove(), 1200);
  }

  function msg(t, ok) {
    $('fb-msg').textContent = t;
    $('fb-msg').className = 'msg ' + (ok ? 'ok' : 'err');
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (text.value.trim().length < 5) { msg('Напиши сообщение хотя бы из пары слов', false); text.focus(); return; }
    const topic = form.querySelector('input[name="topic"]:checked');
    $('fb-send').disabled = true;
    msg('Отправляю…', true);
    try {
      const r = await fetch('/api/feedback', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          topic: topic ? topic.value : 'other',
          name: $('fb-name').value,
          contact: $('fb-contact').value,
          message: text.value,
          website: $('fb-website').value,
        }),
      });
      let data = {};
      try { data = await r.json(); } catch (err) { /* пустой ответ */ }
      if (!r.ok) throw new Error(data.error || 'Не получилось отправить. Попробуй ещё раз.');
      form.reset();
      updateCount();
      msg('Спасибо! Сообщение отправлено.', true);
      launchPlane($('fb-send'));
    } catch (err) {
      msg(err.message === 'Failed to fetch' ? 'Нет связи с сайтом. Проверь интернет и попробуй ещё раз.' : err.message, false);
    }
    $('fb-send').disabled = false;
  });
})();
