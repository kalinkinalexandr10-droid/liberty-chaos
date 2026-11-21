// Анимации сайта: ночной город на первом экране, плавное появление блоков при прокрутке,
// «набегающие» числа. Всё отключается, если в системе включено «уменьшить движение».
(function () {
  const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const SVG = 'http://www.w3.org/2000/svg';

  // ---------- ночной Либерти-Сити за заголовком ----------
  function skyline(host) {
    const W = 1200, H = 260;
    const svg = document.createElementNS(SVG, 'svg');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMax slice');
    svg.setAttribute('aria-hidden', 'true');
    svg.classList.add('skyline-svg');

    // детерминированный «случайный» генератор: город одинаковый при каждой загрузке
    let seed = 7;
    const rnd = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };

    // два слоя домов: дальний (тусклее) и ближний
    [[0.55, 70, 150, 'sky-far'], [1, 40, 210, 'sky-near']].forEach(([alpha, minH, maxH, cls]) => {
      const g = document.createElementNS(SVG, 'g');
      g.setAttribute('class', cls);
      let x = -10;
      while (x < W) {
        const w = 34 + Math.floor(rnd() * 60);
        const h = minH + Math.floor(rnd() * (maxH - minH));
        const b = document.createElementNS(SVG, 'rect');
        b.setAttribute('x', x); b.setAttribute('y', H - h); b.setAttribute('width', w); b.setAttribute('height', h);
        b.setAttribute('class', 'bldg');
        g.appendChild(b);
        // антенна на некоторых высотках
        if (h > maxH * 0.8 && rnd() > 0.4) {
          const a = document.createElementNS(SVG, 'rect');
          a.setAttribute('x', x + w / 2 - 1); a.setAttribute('y', H - h - 18); a.setAttribute('width', 2); a.setAttribute('height', 18);
          a.setAttribute('class', 'bldg');
          g.appendChild(a);
          const tip = document.createElementNS(SVG, 'circle');
          tip.setAttribute('cx', x + w / 2); tip.setAttribute('cy', H - h - 19); tip.setAttribute('r', 2);
          tip.setAttribute('class', 'beacon');
          tip.style.animationDelay = (rnd() * 2).toFixed(2) + 's';
          g.appendChild(tip);
        }
        // окна
        for (let wy = H - h + 10; wy < H - 8; wy += 12) {
          for (let wx = x + 6; wx < x + w - 6; wx += 10) {
            if (rnd() > 0.32) continue;
            const win = document.createElementNS(SVG, 'rect');
            win.setAttribute('x', wx); win.setAttribute('y', wy); win.setAttribute('width', 4); win.setAttribute('height', 5);
            win.setAttribute('class', rnd() > 0.82 ? 'win twinkle' : 'win');
            win.style.opacity = (alpha * (0.45 + rnd() * 0.55)).toFixed(2);
            if (win.classList.contains('twinkle')) {
              win.style.animationDelay = (rnd() * 6).toFixed(2) + 's';
              win.style.animationDuration = (3 + rnd() * 5).toFixed(2) + 's';
            }
            g.appendChild(win);
          }
        }
        x += w + 2 + Math.floor(rnd() * 8);
      }
      svg.appendChild(g);
    });

    host.appendChild(svg);

    // прожекторы и вертолёт — отдельными элементами поверх города
    ['beam beam-a', 'beam beam-b'].forEach((c) => { const d = document.createElement('div'); d.className = c; host.appendChild(d); });
    const heli = document.createElement('div');
    heli.className = 'heli';
    heli.innerHTML = '<svg viewBox="0 0 40 16" aria-hidden="true"><rect class="rotor" x="2" y="1" width="30" height="1.5" rx=".75"/><rect x="16" y="2" width="2" height="3"/><path d="M8 6h14a6 6 0 0 1 0 8H12a4 4 0 0 1-4-4z"/><rect x="22" y="8" width="16" height="2" rx="1"/><circle class="heli-light" cx="13" cy="15" r="1.4"/></svg>';
    host.appendChild(heli);
  }

  // ---------- появление при прокрутке ----------
  let io = null;
  function observe(nodes) {
    const list = Array.prototype.slice.call(nodes || []);
    if (reduce || !('IntersectionObserver' in window)) { list.forEach((n) => { n.classList.add('in'); countUp(n); }); return; }
    if (!io) {
      io = new IntersectionObserver((entries) => {
        entries.forEach((e) => {
          if (!e.isIntersecting) return;
          e.target.classList.add('in');
          countUp(e.target);
          playOnTouch(e.target);
          io.unobserve(e.target);
        });
      }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    }
    list.forEach((n, i) => {
      n.classList.add('reveal');
      n.style.setProperty('--delay', Math.min(i, 8) * 60 + 'ms');
      io.observe(n);
    });
  }

  // на телефоне нет наведения: сценка в кнопке «Скачать» проигрывается сама, когда карточка появилась
  const touch = window.matchMedia && window.matchMedia('(hover: none)').matches;
  function playOnTouch(root) {
    if (!touch || reduce) return;
    root.querySelectorAll('.dl').forEach((b) => setTimeout(() => b.classList.add('play'), 450));
  }

  // числа с атрибутом data-count «набегают» от нуля
  function countUp(root) {
    root.querySelectorAll('[data-count]').forEach((el) => {
      const target = Number(el.getAttribute('data-count')) || 0;
      const suffix = el.getAttribute('data-suffix') || '';
      if (reduce || target === 0) { el.textContent = target + suffix; return; }
      const t0 = performance.now(), dur = 900;
      const step = (t) => {
        const k = Math.min(1, (t - t0) / dur);
        const eased = 1 - Math.pow(1 - k, 3);
        el.textContent = Math.round(target * eased) + suffix;
        if (k < 1) requestAnimationFrame(step);
      };
      requestAnimationFrame(step);
    });
  }

  // кнопки «вдавливаются» при нажатии и дают волну от точки клика
  document.addEventListener('pointerdown', (e) => {
    const btn = e.target.closest && e.target.closest('.btn');
    if (!btn || reduce) return;
    const r = btn.getBoundingClientRect();
    const ripple = document.createElement('span');
    ripple.className = 'ripple';
    ripple.style.left = (e.clientX - r.left) + 'px';
    ripple.style.top = (e.clientY - r.top) + 'px';
    btn.appendChild(ripple);
    setTimeout(() => ripple.remove(), 600);
  });

  document.addEventListener('DOMContentLoaded', () => {
    const host = document.getElementById('skyline');
    if (host) skyline(host);
    observe(document.querySelectorAll('[data-reveal]'));
  });

  window.LCfx = { observe };
})();
