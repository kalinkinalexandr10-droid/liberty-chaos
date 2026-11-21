// Форма обратной связи: отправляет сообщение на /api/feedback.
(function () {
  const $ = (id) => document.getElementById(id);
  const form = $('fb-form');
  const text = $('fb-message');

  const updateCount = () => { $('fb-count').textContent = text.value.length + ' / 2000'; };
  text.addEventListener('input', updateCount);

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
    } catch (err) {
      msg(err.message === 'Failed to fetch' ? 'Нет связи с сайтом. Проверь интернет и попробуй ещё раз.' : err.message, false);
    }
    $('fb-send').disabled = false;
  });
})();
