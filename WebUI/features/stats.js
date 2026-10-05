// «Статистика игры»: play time per pack rebuilt from Minecraft logs (C# side: MainWindow.Stats.cs).
// Decorates installed-pack rows / home cards and adds a 📊 modal with a 14-day chart.
(function(){
  let stats = null;        // { packs: { slug: agg }, main: agg }
  let current = null;      // { slug, title } shown in the modal
  let lastReq = 0;

  const STALE_DAYS = 14;
  const MONTHS = ['янв','фев','мар','апр','мая','июн','июл','авг','сен','окт','ноя','дек'];
  const WEEK = ['вс','пн','вт','ср','чт','пт','сб'];

  document.head.insertAdjacentHTML('beforeend', `<style id="statsCss">
.st-meta{display:block;margin-top:3px;font-family:var(--mono);font-size:10.5px;color:var(--muted)}
.mp-row .grow small.st-meta{display:block;margin-top:4px}
.hp-card .hp-top{position:relative}
.st-chip{position:absolute;top:7px;right:7px;padding:2px 8px;border-radius:999px;font-size:9.5px;font-weight:600;
  color:var(--muted);background:rgba(11,14,16,.6);border:1px solid var(--glass-line);backdrop-filter:blur(6px);pointer-events:auto}
.st-tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:8px}
.st-tile{padding:10px 12px;border-radius:11px;border:1px solid var(--glass-line);background:rgba(255,255,255,.04);min-width:0}
.st-tile small{display:block;font-size:10px;color:var(--muted);text-transform:uppercase;letter-spacing:.06em}
.st-tile b{display:block;font-size:15px;margin-top:4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.st-tile i{display:block;margin-top:2px;font-style:normal;font-size:10.5px;color:var(--muted);font-family:var(--mono);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.st-chart-h{display:flex;justify-content:space-between;align-items:baseline;margin-top:6px;font-size:12px;color:var(--muted)}
.st-chart-h b{color:var(--text);font-size:13px}
.st-chart{display:grid;grid-template-columns:repeat(14,1fr);gap:5px;height:170px;align-items:end;padding:30px 10px 8px;
  border-radius:11px 11px 0 0;border:1px solid var(--glass-line);border-bottom:0;background:rgba(255,255,255,.03)}
.st-col{height:100%;display:flex;flex-direction:column;justify-content:flex-end;min-width:0}
.st-bar{position:relative;min-height:2px;border-radius:4px 4px 2px 2px;
  background:linear-gradient(180deg,var(--grass),color-mix(in srgb,var(--grass) 45%,transparent))}
.st-col.zero .st-bar{background:var(--glass-line)}
.st-col.today .st-bar{box-shadow:0 0 0 1px var(--gold)}
.st-col:hover .st-bar{filter:brightness(1.25)}
.st-col:hover .st-bar::after{content:attr(data-tip);position:absolute;bottom:calc(100% + 5px);left:50%;transform:translateX(-50%);
  white-space:nowrap;padding:4px 8px;border-radius:7px;font-size:11px;background:rgba(11,14,16,.94);
  border:1px solid var(--glass-line);color:var(--text);pointer-events:none;z-index:2}
.st-col:first-child:hover .st-bar::after,.st-col:nth-child(2):hover .st-bar::after{left:0;transform:none}
.st-col:last-child:hover .st-bar::after,.st-col:nth-last-child(2):hover .st-bar::after{left:auto;right:0;transform:none}
.st-labels{display:grid;grid-template-columns:repeat(14,1fr);gap:5px;padding:6px 10px 8px;border-radius:0 0 11px 11px;
  border:1px solid var(--glass-line);border-top:1px solid var(--glass-line);background:rgba(255,255,255,.02);
  font-family:var(--mono);font-size:10px;color:var(--muted);text-align:center;line-height:1.25}
.st-labels span.today{color:var(--gold)}
.st-labels em{display:block;font-style:normal;font-size:9px;opacity:.75}
.st-foot{font-size:11.5px;color:var(--muted);margin-top:4px}
</style>`);

  document.body.insertAdjacentHTML('beforeend', `
<div class="pmodal" id="statsModal" hidden>
  <div class="pm-card glass" style="width:min(620px,100%)">
    <div class="pm-head">
      <b id="stTitle">Статистика игры</b>
      <button class="vw-b vw-close" id="stClose" title="Закрыть">✕</button>
    </div>
    <div class="pm-sub">Время считается по логам Minecraft — даже если во время игры лаунчер был закрыт.</div>
    <div class="pm-body" id="stBody"><div class="empty">Считаю время по логам…</div></div>
  </div>
</div>`);

  const modal = document.getElementById('statsModal');
  const body = document.getElementById('stBody');

  // ── formatting ──
  function plural(n, one, few, many){
    const a = Math.abs(n) % 100, b = a % 10;
    if (a > 10 && a < 20) return many;
    if (b > 1 && b < 5) return few;
    if (b === 1) return one;
    return many;
  }
  function fmtShort(min){
    min = Math.max(0, Math.round(min || 0));
    if (min < 60) return min + ' мин';
    const h = Math.floor(min / 60), m = min % 60;
    return h + ' ч' + (m ? ' ' + m + ' мин' : '');
  }
  function fmtCompact(min){
    min = Math.max(0, Math.round(min || 0));
    return min < 60 ? min + ' мин' : Math.floor(min / 60) + ' ч';
  }
  function fmtLong(min){
    min = Math.max(0, Math.round(min || 0));
    const h = Math.floor(min / 60), m = min % 60;
    const hs = h ? h + ' ' + plural(h, 'час', 'часа', 'часов') : '';
    const ms = m || !h ? m + ' ' + plural(m, 'минута', 'минуты', 'минут') : '';
    return [hs, ms].filter(Boolean).join(' ');
  }
  function parseDay(s){ const [y, mo, d] = (s || '').split('-').map(Number); return new Date(y, mo - 1, d); }
  function startOfDay(d){ return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
  function daysAgo(iso){
    const d = new Date(iso);
    if (!iso || isNaN(d)) return null;
    return Math.round((startOfDay(new Date()) - startOfDay(d)) / 86400000);
  }
  function fmtDate(d, withTime){
    const now = new Date();
    let s = d.getDate() + ' ' + MONTHS[d.getMonth()] + (d.getFullYear() !== now.getFullYear() ? ' ' + d.getFullYear() : '');
    if (withTime) s += ', ' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    return s;
  }
  function agoText(iso){
    const n = daysAgo(iso);
    if (n == null) return '';
    if (n <= 0) return 'сегодня';
    if (n === 1) return 'вчера';
    if (n < 30) return n + ' ' + plural(n, 'день', 'дня', 'дней') + ' назад';
    return fmtDate(new Date(iso), false);
  }

  function aggFor(slug){ return stats && stats.packs ? stats.packs[slug] : null; }

  // ── decorate pack rows / home cards ──
  function decorateRow(el){
    const grow = el.querySelector('.grow');
    if (!grow) return;
    grow.querySelectorAll('.st-meta').forEach(x => x.remove());
    const a = aggFor(el.dataset.slug);
    if (!a) return;
    const line = document.createElement('small');
    line.className = 'st-meta';
    line.textContent = a.sessions
      ? `⏱ ${fmtShort(a.totalMin)} · играл ${agoText(a.lastPlayed)}`
      : '⏱ ещё не играл';
    grow.appendChild(line);
  }

  function decorateCard(el){
    el.querySelectorAll('.st-meta, .st-chip').forEach(x => x.remove());
    const a = aggFor(el.dataset.slug);
    if (!a || !a.sessions) return;
    const meta = el.querySelector('.hp-body small');
    if (meta){
      const line = document.createElement('small');
      line.className = 'st-meta';
      line.textContent = '⏱ ' + fmtCompact(a.totalMin);
      line.title = 'Наиграно: ' + fmtLong(a.totalMin);
      meta.insertAdjacentElement('afterend', line);
    }
    const n = daysAgo(a.lastPlayed);
    const top = el.querySelector('.hp-top');
    if (top && n != null && n >= STALE_DAYS){
      const chip = document.createElement('span');
      chip.className = 'st-chip';
      chip.textContent = 'давно не заходил';
      chip.title = 'Последний раз: ' + fmtDate(new Date(a.lastPlayed), false);
      top.appendChild(chip);
    }
  }

  function decorateAll(){
    if (!stats) return;
    document.querySelectorAll('.mp-row[data-slug]').forEach(decorateRow);
    document.querySelectorAll('.hp-card[data-slug]').forEach(decorateCard);
  }

  document.addEventListener('packs:rendered', decorateAll);

  // ── modal ──
  function renderModal(){
    if (!current) return;
    document.getElementById('stTitle').textContent = 'Статистика · ' + (current.title || current.slug);
    if (!stats){ body.innerHTML = '<div class="empty">Считаю время по логам…</div>'; return; }
    const a = aggFor(current.slug);
    if (!a || !a.sessions){
      body.innerHTML = '<div class="empty">В этой сборке ещё не было игровых сессий — запусти её, и время появится здесь</div>';
      return;
    }

    const days = a.days || [];
    const sum14 = days.reduce((s, d) => s + (d.min || 0), 0);
    const active = days.filter(d => d.min > 0).length;
    const avg = a.sessions ? a.totalMin / a.sessions : 0;
    const peak = Math.max(0, ...days.map(d => d.min || 0));
    const max = Math.max(1, peak);
    const todayKey = (() => { const t = new Date(); return t.getFullYear() + '-' + String(t.getMonth() + 1).padStart(2, '0') + '-' + String(t.getDate()).padStart(2, '0'); })();
    const lastStart = a.lastStart ? new Date(a.lastStart) : null;

    const cols = days.map(d => {
      const dt = parseDay(d.date);
      const pct = d.min > 0 ? Math.max(3, Math.round(d.min / max * 100)) : 0;
      const tip = fmtDate(dt, false) + ' · ' + (d.min > 0 ? fmtLong(d.min) : 'не играл');
      return `<div class="st-col${d.min > 0 ? '' : ' zero'}${d.date === todayKey ? ' today' : ''}">
        <div class="st-bar" style="height:${pct}%" data-tip="${esc(tip)}"></div></div>`;
    }).join('');
    const labels = days.map(d => {
      const dt = parseDay(d.date);
      return `<span${d.date === todayKey ? ' class="today"' : ''}>${dt.getDate()}<em>${WEEK[dt.getDay()]}</em></span>`;
    }).join('');

    const main = stats.main;
    const allMin = Object.values(stats.packs || {}).reduce((s, x) => s + ((x && x.totalMin) || 0), 0) + ((main && main.totalMin) || 0);

    body.innerHTML = `
      <div class="st-tiles">
        <div class="st-tile" title="${esc(fmtLong(a.totalMin))}"><small>Всего</small><b>${esc(fmtShort(a.totalMin))}</b>
          <i>${a.sessions} ${plural(a.sessions, 'сессия', 'сессии', 'сессий')}</i></div>
        <div class="st-tile"><small>В среднем</small><b>${esc(fmtShort(avg))}</b><i>за сессию</i></div>
        <div class="st-tile"><small>За 14 дней</small><b>${esc(fmtShort(sum14))}</b>
          <i>${active} ${plural(active, 'день', 'дня', 'дней')} с игрой</i></div>
        <div class="st-tile" title="${lastStart ? esc(fmtDate(lastStart, true)) : ''}"><small>Последняя сессия</small>
          <b>${esc(agoText(a.lastPlayed))}</b>
          <i>${lastStart ? esc(fmtDate(lastStart, true)) + ' · ' : ''}${esc(fmtShort(a.lastMin))}</i></div>
      </div>
      <div class="st-chart-h"><span>Последние 14 дней</span><span>макс. <b>${esc(fmtShort(peak))}</b> в день</span></div>
      <div>
        <div class="st-chart">${cols}</div>
        <div class="st-labels">${labels}</div>
      </div>
      ${sum14 ? '' : '<div class="st-foot">За последние две недели сюда не заходил.</div>'}
      <div class="st-foot">Основная игра: ${esc(main && main.sessions ? fmtShort(main.totalMin) : 'не запускалась')} · всего везде: ${esc(fmtShort(allMin))}</div>`;
  }

  function open(slug, title){
    current = { slug, title };
    renderModal();
    modal.removeAttribute('hidden');
    request(true);
  }
  function close(){ modal.setAttribute('hidden', ''); current = null; }

  document.getElementById('stClose').onclick = close;
  modal.addEventListener('click', e => { if (e.target === modal) close(); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !modal.hidden) close(); });

  // ── data ──
  function request(force){
    const now = Date.now();
    if (!force && now - lastReq < 60000) return;
    lastReq = now;
    send({ type: 'statsLoad' });
  }

  if (host) host.addEventListener('message', e => {
    const m = e.data;
    if (!m) return;
    // a session just ended — its minutes are in the logs now
    if (m.type === 'gameState' && m.state === 'idle') { setTimeout(() => request(true), 1500); return; }
    if (m.type !== 'stats') return;
    stats = { packs: m.packs || {}, main: m.main || null };
    decorateAll();
    if (current && !modal.hidden) renderModal();
  });

  // logs grow while the launcher sits in the background — refresh when the user comes back
  window.addEventListener('focus', () => request(false));
  document.addEventListener('visibilitychange', () => { if (!document.hidden) request(false); });

  registerPackAction({
    key: 'stats', label: '📊', title: 'Статистика игры',
    onClick(pack){ if (pack) open(pack.slug, pack.title); }
  });
  send({ type: 'loadInstalledPacks' });
  // the startup push from C# may land before this script loaded
  if (!stats) request(true);
})();
