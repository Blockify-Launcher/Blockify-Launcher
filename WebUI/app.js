'use strict';
// ── bridge ──
const host = window.chrome && window.chrome.webview;
function send(msg){ if (host) host.postMessage(msg); }

// ── navigation ──
let modsLoaded = false;
function goTo(s){
  const navKey = s === 'builder' ? 'packs' : s;
  document.querySelectorAll('.nav button').forEach(b => b.classList.toggle('active', b.dataset.s === navKey));
  document.querySelectorAll('.screen').forEach(x => x.classList.remove('active'));
  const el = document.getElementById('s-' + s);
  if (el) el.classList.add('active');
  // on-demand data for the feature screens
  if (s === 'home') send({ type: 'loadInstalledPacks' });
  else if (s === 'worlds') send({ type: 'loadWorlds' });
  else if (s === 'screens') send({ type: 'loadScreens' });
  else if (s === 'accounts') send({ type: 'loadSkins' });
  else if (navKey === 'packs') {
    send({ type: 'loadInstalledPacks' });
    if (!modsLoaded) { modsLoaded = true; send({ type: 'loadMods' }); }
  }
}
document.querySelectorAll('.nav button').forEach(b => b.onclick = () => goTo(b.dataset.s));
document.getElementById('accountCard').onclick = () => goTo('accounts');

// ── window chrome ──
document.getElementById('winMin').onclick = () => send({ type: 'min' });
document.getElementById('winMax').onclick = () => send({ type: 'max' });
document.getElementById('winClose').onclick = () => send({ type: 'close' });
// drag by the title bar (ignoring the control buttons) + sidebar logo
const titlebar = document.getElementById('titlebar');
titlebar.addEventListener('mousedown', e => { if (e.button === 0 && !e.target.closest('.wb')) send({ type: 'drag' }); });
titlebar.addEventListener('dblclick', e => { if (!e.target.closest('.wb')) send({ type: 'max' }); });
const logoDrag = document.getElementById('logoDrag');
if (logoDrag) logoDrag.addEventListener('mousedown', e => { if (e.button === 0) send({ type: 'drag' }); });
document.querySelectorAll('.rsz').forEach(el =>
  el.addEventListener('mousedown', e => { if (e.button === 0) send({ type: 'resize', dir: el.dataset.d }); }));

// ── toolbar chips (generic) ──
function wireChips(containerId, onPick){
  const c = document.getElementById(containerId);
  if (!c) return;
  c.querySelectorAll('.chip').forEach(chip => chip.onclick = () => {
    c.querySelectorAll('.chip').forEach(x => x.classList.remove('on'));
    chip.classList.add('on');
    onPick(chip.dataset.f);
  });
}

// ── helpers ──
const esc = s => (s ?? '').toString().replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));

// ── unified glass dialog (replaces native confirm/alert; also shows C# errors) ──
const Dialog = (function(){
  const root = document.getElementById('dlg');
  const okBtn = document.getElementById('dlgOk');
  const cancelBtn = document.getElementById('dlgCancel');
  const more = document.getElementById('dlgMore');
  let resolver = null, returnFocus = null;
  let queue = Promise.resolve();   // one dialog at a time: a second call waits instead of eating the first
  function show(msg, title, opts){
    opts = opts || {};
    document.getElementById('dlgTitle').textContent = title || 'Blockify';
    document.getElementById('dlgMsg').textContent = msg || '';
    const details = (opts.details || '').toString().trim();
    document.getElementById('dlgDetails').textContent = details;
    more.open = false;
    more.hidden = !details || details === (msg || '').trim();
    cancelBtn.style.display = opts.confirm ? '' : 'none';
    okBtn.textContent = opts.okText || 'ОК';
    cancelBtn.textContent = opts.cancelText || 'Отмена';
    okBtn.classList.toggle('danger', !!opts.danger);
    okBtn.classList.toggle('primary', !opts.danger);
    returnFocus = document.activeElement;
    root.removeAttribute('hidden');
    // irreversible actions: the safe choice has the focus, so a stray Enter does nothing harmful
    (opts.confirm && opts.danger ? cancelBtn : okBtn).focus();
    return new Promise(res => { resolver = res; });
  }
  function open(msg, title, opts){
    const p = queue.then(() => show(msg, title, opts));
    queue = p.catch(() => {});
    return p;
  }
  function done(val){
    root.setAttribute('hidden','');
    const r = resolver; resolver = null;
    try { if (returnFocus && returnFocus.focus && document.contains(returnFocus)) returnFocus.focus(); } catch(e){}
    if (r) r(val);
  }
  okBtn.onclick = () => done(true);
  cancelBtn.onclick = () => done(false);
  root.addEventListener('click', e => { if (e.target === root) done(false); });
  // capture phase: while the dialog is up, keys belong to it (Esc must not also close the viewer below)
  window.addEventListener('keydown', e => {
    if (root.hasAttribute('hidden')) return;
    if (e.key === 'Escape'){ e.preventDefault(); e.stopImmediatePropagation(); done(false); return; }
    if (e.key === 'Enter'){
      // Enter on a focused button = that button's own click (Enter on «Отмена» cancels)
      const a = document.activeElement;
      if (a && a.tagName === 'BUTTON' && root.contains(a)) { e.stopImmediatePropagation(); return; }
      if (a && a.tagName === 'SUMMARY' && root.contains(a)) { e.stopImmediatePropagation(); return; }
      e.preventDefault(); e.stopImmediatePropagation();
      done(true);
      return;
    }
    if (e.key === 'Tab'){
      // keep focus inside the dialog
      const f = [...root.querySelectorAll('button, summary')].filter(x => x.offsetParent !== null);
      if (!f.length) return;
      const i = f.indexOf(document.activeElement);
      const next = e.shiftKey ? (i <= 0 ? f.length - 1 : i - 1) : (i < 0 || i >= f.length - 1 ? 0 : i + 1);
      e.preventDefault(); f[next].focus();
      return;
    }
    e.stopImmediatePropagation();
  }, true);
  return {
    alert: (msg, title, opts) => open(msg, title, Object.assign({}, opts || {}, { confirm: false })),
    confirm: (msg, title, opts) => open(msg, title, Object.assign({ confirm: true }, opts || {})),
    isOpen: () => !root.hasAttribute('hidden')
  };
})();

// ── human-readable errors: the engine and .NET speak English, the player shouldn't have to ──
const ERROR_RULES = [
  [/No such host|name.*(not|could not) be resolved|NameResolution|Этот хост неизвестен|host is known|Имя узла/i,
    'Нет соединения с интернетом или сервер недоступен. Проверь подключение и попробуй ещё раз.'],
  [/actively refused|connection (was )?(refused|reset|closed|aborted)|Unable to connect|forcibly closed|SocketException|sending the request|network is unreachable|Подключение не установлено|разорвано/i,
    'Не удалось связаться с сервером. Проверь интернет (или VPN/антивирус) и попробуй ещё раз.'],
  [/timed? ?out|Timeout|HttpClient\.Timeout|время ожидания/i,
    'Сервер слишком долго не отвечает. Попробуй ещё раз чуть позже.'],
  [/SSL|TLS|certificate|сертификат/i,
    'Не удалось установить защищённое соединение. Проверь дату и время на компьютере, а также антивирус или прокси.'],
  [/\b429\b|Too Many Requests/i, 'Слишком много запросов к серверу. Подожди минуту и попробуй снова.'],
  [/\b404\b|Not Found/i, 'Файл не найден на сервере — возможно, его удалили. Попробуй другую версию.'],
  [/\b403\b|Forbidden/i, 'Сервер отказал в доступе (403). Попробуй позже или через другую сеть.'],
  [/\b50[0-4]\b|Service Unavailable|Bad Gateway|Internal Server Error|Gateway Time/i,
    'Сервер временно не работает. Попробуй через несколько минут.'],
  [/being used by another process|используется другим процессом|sharing violation|locked/i,
    'Файл занят другой программой — возможно, запущена игра. Закрой её и попробуй снова.'],
  [/Access to the path.*denied|UnauthorizedAccess|Access is denied|Отказано в доступе/i,
    'Нет доступа к файлу или папке. Проверь, что папка не защищена от записи и не заблокирована антивирусом.'],
  [/not enough space|disk (is )?full|Недостаточно места/i, 'На диске закончилось место. Освободи место и попробуй снова.'],
  [/Could not find (file|a part of the path)|FileNotFound|DirectoryNotFound|не удается найти/i,
    'Не найден нужный файл или папка. Попробуй «Починить» сборку или переустановить версию.'],
  [/end of central directory|Central Directory|InvalidDataException|corrupt|zip/i,
    'Архив повреждён или скачался не полностью. Попробуй скачать ещё раз.'],
  [/hash|sha1|sha512|checksum/i, 'Файл скачался повреждённым (не совпала контрольная сумма). Попробуй ещё раз.'],
  [/java|jre|jvm/i, 'Проблема с Java. Оставь в настройках поле Java пустым — лаунчер подберёт её сам.'],
  [/2148916233/, 'У этого аккаунта Microsoft нет профиля Xbox. Зайди на xbox.com, создай профиль и попробуй снова.'],
  [/2148916238/, 'Аккаунт принадлежит ребёнку: взрослый должен добавить его в семейную группу Microsoft.'],
  [/2148916235|2148916236|2148916237/, 'Xbox Live недоступен в твоей стране или требует подтверждения возраста.'],
  [/user.*cancel|cancell?ed by user|access_denied/i, 'Вход отменён.'],
  [/xbox|xsts|msal|oauth|microsoft/i, 'Не удалось войти через Microsoft. Попробуй ещё раз позже.'],
  [/canceled|cancelled/i, 'Операция прервана. Попробуй ещё раз.'],
];
// → { text: what the player reads, details: original message (for «Подробнее» / bug reports) }
function humanizeError(raw){
  const msg = (raw == null ? '' : String(raw)).trim();
  if (!msg) return { text: 'Что-то пошло не так. Попробуй ещё раз.', details: '' };
  // C# marks network failures with the short code 'offline'
  if (msg === 'offline') return { text: 'нет соединения с интернетом или сервер недоступен. Проверь подключение и попробуй ещё раз.', details: '' };
  // already written for people (our own C# messages are Russian)
  if (/[а-яё]/i.test(msg) && !/Exception/.test(msg)) return { text: msg, details: '' };
  for (const [re, text] of ERROR_RULES) if (re.test(msg)) return { text, details: msg };
  return { text: 'Что-то пошло не так. Подробности — ниже и в журнале лаунчера (Настройки → О программе).', details: msg };
}
function showError(raw, title){
  const h = humanizeError(raw);
  return Dialog.alert(h.text, title || 'Ошибка', { details: h.details });
}

// ── tiny toast for confirmations that need no button ──
const Toast = (function(){
  const el = document.getElementById('toast');
  let t;
  return { show(text, ms){
    if (!el) return;
    el.textContent = text; el.classList.add('show');
    clearTimeout(t); t = setTimeout(() => el.classList.remove('show'), ms || 2600);
  }};
})();

// small persisted prefs (may be unavailable — never let storage break the UI)
function lsGet(k){ try { return localStorage.getItem(k); } catch(e){ return null; } }
function lsSet(k, v){ try { localStorage.setItem(k, v); } catch(e){} }

// ── HOME: play + version ──
// what the player launched last: { kind:'pack', slug, title } | { kind:'version', name }
function rememberTarget(t){ lsSet('blockify.lastTarget', JSON.stringify(t)); }
function lastTarget(){ try { return JSON.parse(lsGet('blockify.lastTarget') || 'null'); } catch(e){ return null; } }
function launchPack(slug, title){
  if (!slug || GameState.isBusy(slug)) return;
  if (Onboard.hasAccount === false){ askForAccount(); return; }
  rememberTarget({ kind: 'pack', slug, title: title || slug });
  send({ type: 'launchPack', slug });
}

// first run: nothing to play with yet → explain and lead to the right screen
const Onboard = { hasAccount: null, hasVersion: null };
function openAccountsForNew(){
  goTo('accounts');
  setTimeout(() => { const i = document.getElementById('offNick'); if (i) i.focus(); }, 80);
}
function askForAccount(){
  Dialog.confirm('Чтобы играть, нужен профиль: оффлайн-профиль по нику или вход через Microsoft. Это займёт полминуты.',
    'Добавь профиль', { okText: 'К аккаунтам', cancelText: 'Позже' })
    .then(ok => { if (ok) openAccountsForNew(); });
}
function askForVersion(){
  Dialog.confirm('Не выбрана версия игры. Установи версию Minecraft на экране «Версии» (например, свежий релиз) или поставь готовую сборку.',
    'Нужна версия игры', { okText: 'К версиям', cancelText: 'Позже' })
    .then(ok => { if (ok) goTo('versions'); });
}
function updateOnboarding(){
  const card = document.getElementById('onboard');
  const acts = document.getElementById('obActs');
  if (!card) return;
  let html = '';
  if (Onboard.hasAccount === false){
    document.getElementById('obIcon').textContent = '👤';
    document.getElementById('obTitle').textContent = 'Добавь профиль, чтобы играть';
    document.getElementById('obText').textContent = 'Оффлайн-профиль по нику или вход через Microsoft — это займёт полминуты.';
    html = '<button class="btn primary" data-ob="offline">Создать профиль</button><button class="btn" data-ob="ms">Войти через Microsoft</button>';
  } else if (Onboard.hasVersion === false){
    document.getElementById('obIcon').textContent = '⬇';
    document.getElementById('obTitle').textContent = 'Установи версию игры';
    document.getElementById('obText').textContent = 'Выбери версию Minecraft или готовую сборку — после установки нажми «Играть».';
    html = '<button class="btn primary" data-ob="versions">К версиям</button><button class="btn" data-ob="packs">Сборки</button>';
  }
  card.hidden = !html;
  acts.innerHTML = html;
  acts.querySelectorAll('[data-ob]').forEach(b => b.onclick = () => {
    const a = b.dataset.ob;
    if (a === 'offline') openAccountsForNew();
    else if (a === 'ms') { goTo('accounts'); send({ type: 'msLogin' }); }
    else goTo(a);
  });
  updateHero();
}
function updateHero(){
  const t = document.getElementById('heroTitle'), p = document.getElementById('heroSub');
  let title = 'Готов к запуску', sub = 'Выбери версию и нажми «Играть».';
  const run = GameState.label();
  if (run){ title = run.title; sub = run.sub; }
  else if (Onboard.hasAccount === false){ title = 'Добро пожаловать в Blockify'; sub = 'Добавь профиль — и можно играть.'; }
  else if (Onboard.hasVersion === false){ title = 'Почти готово'; sub = 'Осталось установить версию игры.'; }
  t.textContent = title; p.textContent = sub;
}

// «▶ Продолжить: <сборка>» — one click back into the pack played last time
const contBtn = document.getElementById('contBtn');
function updateContinue(){
  const lt = lastTarget();
  const pack = lt && lt.kind === 'pack' ? (installedPacksCache || []).find(p => p.slug === lt.slug) : null;
  if (!pack){ contBtn.hidden = true; return; }
  contBtn.hidden = false;
  contBtn.dataset.slug = pack.slug;
  contBtn.textContent = '▶ Продолжить: ' + (pack.title || pack.slug);
  contBtn.disabled = GameState.isBusy(pack.slug);
}
contBtn.onclick = () => {
  const pack = (installedPacksCache || []).find(p => p.slug === contBtn.dataset.slug);
  if (pack) launchPack(pack.slug, pack.title);
};

document.getElementById('playBtn').onclick = () => {
  if (GameState.isBusy('')) return;
  if (Onboard.hasAccount === false){ askForAccount(); return; }
  if (Onboard.hasVersion === false || (Onboard.hasVersion && !versionSel)){ askForVersion(); return; }
  if (versionSel){
    // the launcher's hidden selector can drift (a pack launch switches it) — pin what the player sees
    send({ type: 'selectVersion', name: versionSel });
    rememberTarget({ kind: 'version', name: versionSel });
    lsSet('blockify.lastVersion', versionSel);
  }
  send({ type: 'launch' });
};

// game state from C#: {type:'gameState', state:'starting'|'running'|'idle', slug} ('' / none = main game)
const GameState = (function(){
  let starting = null;          // slug being launched, null = nothing
  const running = new Set();    // slugs with a live game
  let launchBusy = false;       // legacy {type:'launchState', busy}
  const key = s => (s == null ? '' : String(s));
  function set(m){
    const s = key(m.slug);
    if (m.state === 'starting') starting = s;
    else if (m.state === 'running'){ running.add(s); if (starting === s) starting = null; }
    else if (m.state === 'idle'){
      if (m.slug == null){ running.clear(); starting = null; }
      else { running.delete(s); if (starting === s) starting = null; }
    }
    apply();
  }
  function setBusy(b){ launchBusy = !!b; apply(); }
  const anyStarting = () => starting !== null || launchBusy;
  // a target can't be launched while any launch is in flight or while it is itself running
  const isBusy = slug => anyStarting() || running.has(key(slug));
  function packTitle(slug){
    const p = (installedPacksCache || []).find(x => x.slug === slug);
    return p ? (p.title || slug) : slug;
  }
  function label(){
    if (anyStarting()){
      const s = starting;
      return { title: 'Запускаем игру…', sub: s ? `Сборка «${packTitle(s)}» запускается — первое открытие может занять минуту.` : 'Проверяю файлы и запускаю Minecraft.' };
    }
    if (!running.size) return null;
    const packs = [...running].filter(Boolean);
    if (!packs.length) return { title: 'Игра запущена', sub: 'Приятной игры! Лаунчер можно свернуть.' };
    return { title: 'Игра запущена', sub: `Сейчас запущена сборка «${packTitle(packs[0])}». Приятной игры!` };
  }
  function markPacks(){
    document.querySelectorAll('.mp-row[data-slug], .hp-card[data-slug]').forEach(el => {
      const s = el.dataset.slug, on = running.has(s), st = starting === s;
      el.classList.toggle('running', on);
      const b = el.querySelector('[data-a=play], [data-act=play]');
      if (b){
        b.disabled = isBusy(s);
        b.textContent = on ? '● В игре' : st ? 'Запуск…' : '▶ Играть';
      }
      // nothing destructive while the game holds the instance's files
      el.querySelectorAll('[data-a=remove], [data-a=reinstall], [data-act=rm]').forEach(x => {
        x.disabled = on || st;
        if (on || st) x.title = 'Недоступно, пока игра запущена'; else x.removeAttribute('title');
      });
      const nameEl = el.querySelector('.grow b, .hp-body b');
      const badge = el.querySelector('.run-badge');
      if (on && nameEl && !badge) nameEl.insertAdjacentHTML('beforeend', '<span class="run-badge">в игре</span>');
      if (!on && badge) badge.remove();
    });
  }
  function apply(){
    const b = document.getElementById('playBtn');
    const mainRunning = running.has('');
    b.classList.toggle('running', mainRunning && !anyStarting());
    if (anyStarting()){ b.textContent = 'ЗАПУСКАЕТСЯ…'; b.disabled = true; b.title = ''; }
    else if (mainRunning){ b.textContent = '● ИГРА ЗАПУЩЕНА'; b.disabled = true; b.title = 'Игра уже запущена. Закрой её, чтобы запустить снова.'; }
    else { b.textContent = '▶  ИГРАТЬ'; b.disabled = false; b.title = ''; }
    document.body.classList.toggle('game-on', running.size > 0);
    markPacks();
    updateContinue();
    updateHero();
  }
  document.addEventListener('packs:rendered', markPacks);
  return { set, setBusy, isBusy, label, apply };
})();

// ── custom glass dropdowns (menu portalled to <body> so overflow:hidden can't clip it) ──
let activeDrop = null;
function closeDrops(){
  if (!activeDrop) return;
  activeDrop.menu.remove();
  activeDrop.wrap.classList.remove('open');
  activeDrop = null;
}
function openDrop(wrap, build){
  closeDrops();
  const rect = wrap.getBoundingClientRect();
  const menu = document.createElement('div');
  menu.className = 'gdd-menu gdd-portal';
  build(menu);
  document.body.appendChild(menu);
  menu.style.left = rect.left + 'px';
  menu.style.top = (rect.bottom + 6) + 'px';
  menu.style.minWidth = rect.width + 'px';
  wrap.classList.add('open');
  activeDrop = { menu, wrap };
  const mr = menu.getBoundingClientRect();
  if (mr.bottom > window.innerHeight - 10) menu.style.top = Math.max(10, rect.top - mr.height - 6) + 'px';
}
document.addEventListener('click', e => { if (!e.target.closest('.gdd') && !e.target.closest('.gdd-portal')) closeDrops(); });
window.addEventListener('resize', closeDrops);
// close on page scroll, but NOT when scrolling inside the open menu itself
document.addEventListener('scroll', e => {
  if (activeDrop && activeDrop.menu.contains(e.target)) return;
  closeDrops();
}, true);

// version dropdown
let versionNames = [], versionSel = '';
const verSelect = document.getElementById('verSelect');
verSelect.addEventListener('click', () => {
  if (verSelect.classList.contains('open')) { closeDrops(); return; }
  openDrop(verSelect, menu => {
    menu.innerHTML = versionNames.map(n => `<div class="${n === versionSel ? 'sel' : ''}">${esc(n)}</div>`).join('');
    [...menu.children].forEach((d, i) => d.onclick = () => {
      versionSel = versionNames[i];
      userVersion = versionSel;
      lsSet('blockify.lastVersion', versionSel);
      document.getElementById('verValue').textContent = versionSel;
      closeDrops();
      send({ type: 'selectVersion', name: versionSel });
    });
  });
});
// the version the player chose (this session, or last time) wins over the launcher's default pick
let userVersion = lsGet('blockify.lastVersion') || '';
function fillVersionSelect(names, selected){
  versionNames = names || [];
  versionSel = selected || versionNames[0] || '';
  if (userVersion && versionNames.includes(userVersion) && userVersion !== versionSel){
    versionSel = userVersion;
    send({ type: 'selectVersion', name: versionSel });
  }
  document.getElementById('verValue').textContent = versionSel || '—';
  Onboard.hasVersion = versionNames.length > 0;
  updateOnboarding();
}

// enhance native <select> (settings) into a glass dropdown
function enhanceSelect(sel){
  if (sel.dataset.enh) return; sel.dataset.enh = '1';
  const wrap = document.createElement('div'); wrap.className = 'gdd gdd-field';
  const trigger = document.createElement('div'); trigger.className = 'gdd-trigger';
  trigger.innerHTML = '<span class="gdd-val"></span><span class="caret">▾</span>';
  sel.style.display = 'none';
  sel.parentNode.insertBefore(wrap, sel);
  wrap.append(trigger, sel);
  const label = () => trigger.querySelector('.gdd-val').textContent = sel.options[sel.selectedIndex] ? sel.options[sel.selectedIndex].text : '';
  trigger.onclick = () => {
    if (wrap.classList.contains('open')) { closeDrops(); return; }
    openDrop(wrap, menu => {
      menu.innerHTML = [...sel.options].map((o, i) => `<div class="${i === sel.selectedIndex ? 'sel' : ''}">${esc(o.text)}</div>`).join('');
      [...menu.children].forEach((d, i) => d.onclick = () => { sel.selectedIndex = i; sel.dispatchEvent(new Event('change')); label(); closeDrops(); });
    });
  };
  sel._render = label; label();
}
document.querySelectorAll('.set-card select').forEach(enhanceSelect);

// ── VERSIONS screen ──
let allVersions = [];
function renderVersions(filter){
  const rows = allVersions.filter(v =>
    filter === 'all' ? true :
    filter === 'old' ? (v.kind === 'old_beta' || v.kind === 'old_alpha') :
    v.kind === filter);
  const list = document.getElementById('versionsList');
  if (!rows.length){ list.innerHTML = '<div class="empty">Ничего не найдено</div>'; return; }
  list.innerHTML = rows.slice(0, 200).map(v => {
    const cls = v.kind === 'snapshot' ? 'vic-snap' : (v.kind === 'old_beta' || v.kind === 'old_alpha') ? 'vic-forge' : 'vic-rel';
    const icon = v.kind === 'snapshot' ? 'SNP' : v.kind === 'old_beta' ? 'BETA' : v.kind === 'old_alpha' ? 'ALFA' : 'REL';
    const installed = v.badge === 'установлена';
    return `<div class="vrow glass" style="background:var(--glass2)">
      <div class="vicon ${cls}">${icon}</div>
      <div class="grow"><b>${esc(v.name)}</b><small>${esc(v.info)}</small></div>
      ${installed
        ? '<span class="badge">установлена</span>'
        : `<button class="btn" data-inst="${esc(v.name)}">Установить</button>`}
    </div>`;
  }).join('');
  list.querySelectorAll('[data-inst]').forEach(b => b.onclick = () => {
    b.textContent = 'Установка…'; b.classList.add('installing');
    JobPanel.update('ver:' + b.dataset.inst, b.dataset.inst, 'Установка версии…', 0, 0);
    send({ type: 'installVersion', name: b.dataset.inst });
  });
}
wireChips('verFilters', f => renderVersions(f));

// ── PACKS screen: catalog search + filters ──
let packLoader = '', packType = 'modpack';
let packSeq = 0;   // request number: only the answer to the latest search is drawn
function doPackSearch(){
  const q = document.getElementById('packQuery').value.trim();
  const mc = document.getElementById('packMc').value.trim();
  const sort = document.getElementById('packSort').value;
  document.getElementById('packsList').innerHTML = '<div class="empty" style="grid-column:1/-1">Поиск…</div>';
  send({ type: 'searchPacks', query: q, loader: packLoader, gameVersion: mc, sort, ptype: packType, seq: ++packSeq });
}
wireChips('packTypes', t => {
  packType = t;
  // loader chips only apply to mods/modpacks
  document.getElementById('packFilters').style.display = (t === 'modpack' || t === 'mod') ? '' : 'none';
  doPackSearch();
});

// ── Time machine: snapshots timeline for a pack ──
const TimeMachine = (function(){
  const root = document.getElementById('snapModal'), body = document.getElementById('snBody');
  let slug = '';
  function open(packSlug, title){
    slug = packSlug;
    document.getElementById('snTitle').textContent = '⏳ Машина времени — ' + (title || packSlug);
    document.getElementById('snLabel').value = '';
    body.innerHTML = '<div class="empty">Загрузка…</div>';
    root.removeAttribute('hidden');
    send({ type: 'loadSnapshots', slug });
  }
  function close(){ root.setAttribute('hidden',''); slug = ''; }
  function render(forSlug, items){
    if (!slug || slug !== forSlug) return;
    if (!items || !items.length){ body.innerHTML = '<div class="empty">Снимков пока нет — сделай первый кнопкой выше: он станет профилем сборки</div>'; return; }
    const row = (s, manual) => `
      <div class="sn-row${s.same ? ' cur' : ''}${manual ? ' manual' : ''}">
        <span class="sn-dot" title="${manual ? 'профиль' : 'авто-снимок'}"></span>
        <div class="grow"><b>${esc(s.label || s.reason || 'снимок')}</b>
          <small>${esc(s.at)} · ${s.enabled}/${s.mods} модов вкл.${s.hasConfig ? ' · конфиги' : ''}</small></div>
        <div class="sn-diff" title="Отличия от текущего состояния">${s.same ? '<span class="p">= текущее</span>' :
          `${s.added ? `<span class="p">+${s.added}</span>` : ''}${s.removed ? `<span class="m">−${s.removed}</span>` : ''}${s.toggled ? `<span class="t">~${s.toggled}</span>` : ''}`}</div>
        ${s.same ? '' : `<button class="btn primary" data-restore="${esc(s.id)}">${manual ? 'Переключиться' : 'Откатить'}</button>`}
        <button class="btn danger" data-del="${esc(s.id)}" title="Удалить">✕</button>
      </div>`;
    const manual = items.filter(s => !s.auto), auto = items.filter(s => s.auto);
    body.innerHTML =
      `<div class="mm-count">Профили — именованные наборы модов и конфигов; переключение меняет содержимое сборки без переустановки</div>` +
      (manual.length ? manual.map(s => row(s, true)).join('') : '<div class="empty" style="padding:14px">Профилей нет — «📸 Снимок сейчас» с подписью создаст первый</div>') +
      (auto.length ? `<div class="mm-count" style="margin-top:10px">История (авто-снимки перед изменениями)</div>` + auto.map(s => row(s, false)).join('') : '');
    body.querySelectorAll('[data-restore]').forEach(b => b.onclick = () =>
      Dialog.confirm('Вернуть сборку к этому снимку? Текущее состояние сохранится отдельным снимком «перед откатом».', 'Откат сборки', { okText: 'Откатить' })
        .then(ok => { if (!ok) return; JobPanel.update('restore:' + slug, 'Откат сборки', 'Восстанавливаю…', 0, 0); send({ type: 'restoreSnapshot', slug, id: b.dataset.restore }); }));
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = () => {
      const s = items.find(x => x.id === b.dataset.del);
      const name = s ? (s.label || s.reason || 'снимок') : 'снимок';
      Dialog.confirm(`Удалить ${s && !s.auto ? 'профиль' : 'снимок'} «${name}»? Восстановить его будет нельзя.`, 'Удаление снимка',
        { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type: 'deleteSnapshot', slug, id: b.dataset.del }); });
    });
  }
  document.getElementById('snMake').onclick = () => {
    body.innerHTML = '<div class="empty">Считаю хэши модов…</div>';
    send({ type: 'makeSnapshot', slug, label: document.getElementById('snLabel').value.trim() });
  };
  document.getElementById('snClose').onclick = close;
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { open, close, render };
})();

// ── Crash Doctor: diagnosis + one-click fixes for a pack ──
const CrashDoctor = (function(){
  const root = document.getElementById('crashModal'), body = document.getElementById('cdBody');
  let slug = '';
  function show(m){
    slug = m.slug;
    document.getElementById('cdTitle').textContent = '🩺 Crash Doctor — ' + (m.packTitle || slug);
    const r = m.result || {};
    document.getElementById('cdSub').textContent = r.found
      ? `${r.file} · ${r.when} · ${r.mods} модов в сборке${m.auto ? ' · игра завершилась с ошибкой' : ''}`
      : '';
    // confidence 'low' = no real crash report, the guess comes from the game log
    const soft = r.confidence === 'low'
      ? `<div class="cd-note">Возможная причина — точного краш-отчёта нет, вывод сделан по журналу игры${r.source === 'log' ? ' (latest.log)' : ''}.</div>`
      : '';
    if (!r.found){ body.innerHTML = `<div class="empty">${esc(r.headline || 'Нет данных')}</div>`; }
    else body.innerHTML = soft + `<div class="cd-head${r.confidence === 'low' ? ' low' : ''}">${esc(r.headline)}</div>` + (r.items || []).map((it, i) => `
      <div class="cd-item">
        <b>${esc(it.title)}</b><p>${esc(it.detail)}</p>
        <div class="cd-fixes">${(it.fixes || []).map((f, j) =>
          `<button class="btn ${f.kind === 'disable' || f.kind === 'install' || f.kind === 'ram' ? 'primary' : ''}" data-i="${i}" data-j="${j}">${esc(f.label)}</button>`).join('')}</div>
      </div>`).join('');
    body.querySelectorAll('[data-i]').forEach(b => b.onclick = async () => {
      const f = r.items[+b.dataset.i].fixes[+b.dataset.j];
      if (f.kind === 'install' && !await Dialog.confirm(
          `Скачать с Modrinth мод «${f.slug}» и его обязательные зависимости в эту сборку? Перед этим сохранится снимок для отката.`,
          'Установка зависимости', { okText: 'Установить' })) return;
      b.textContent = '…'; b.classList.add('done');
      send({ type: 'crashFix', slug, kind: f.kind, file: f.file || '', modSlug: f.slug || '', ram: f.ram || 0 });
      b.dataset.label = f.label;
    });
    root.removeAttribute('hidden');
  }
  function fixDone(m){
    if (m.slug !== slug) return;
    body.querySelectorAll('.btn.done').forEach(b => {
      if (b.textContent === '…'){
        if (m.ok) b.textContent = '✓ ' + (b.dataset.label || 'Готово');
        else { const h = humanizeError(m.error); b.textContent = '✕ Не получилось'; b.title = h.text + (h.details ? ' — ' + h.details : ''); }
      }
    });
  }
  function close(){ root.setAttribute('hidden',''); slug = ''; }
  document.getElementById('cdClose').onclick = close;
  document.getElementById('cdLogs').onclick = () => send({ type: 'crashFix', slug, kind: 'openLogs' });
  document.getElementById('cdLaunch').onclick = () => {
    const s = slug, p = (installedPacksCache || []).find(x => x.slug === s);
    close(); launchPack(s, p && p.title);
  };
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { show, fixDone, close };
})();

// choose which installed pack receives a mod / shader / resource pack
let installedPacksCache = [];
const InstallTo = (function(){
  const root = document.getElementById('installToModal'), body = document.getElementById('itBody');
  let item = null;
  const KIND = { mod: 'мод', shader: 'шейдер', resourcepack: 'ресурспак' };
  function open(p){
    item = p;
    document.getElementById('itTitle').textContent = `${KIND[p.ptype] || 'файл'} «${p.title}» — в какую сборку?`;
    if (!installedPacksCache.length){ body.innerHTML = '<div class="empty">Нет установленных сборок — сначала поставь любую</div>'; }
    else body.innerHTML = installedPacksCache.map((k, i) => `
      <div class="pv-row pv-pick" data-i="${i}">
        <div class="mp-ico" style="width:34px;height:34px${k.icon ? `;background-image:url('${esc(k.icon)}')` : ''}"></div>
        <div class="grow" style="flex:1"><b>${esc(k.title)}</b><small><span class="pv-badge">${esc(k.loader)}</span> ${esc(k.mc)}</small></div>
      </div>`).join('');
    body.querySelectorAll('.pv-pick').forEach(r => r.onclick = () => {
      const k = installedPacksCache[+r.dataset.i];
      JobPanel.update(`content:${item.ptype}:${item.slug}`, item.title + ' → ' + k.title, 'Ищу версию…', 0, 0);
      send({ type: 'installContent', slug: item.slug, title: item.title, ptype: item.ptype, packSlug: k.slug });
      close();
    });
    root.removeAttribute('hidden');
  }
  function close(){ root.setAttribute('hidden',''); item = null; }
  document.getElementById('itClose').onclick = close;
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { open, close };
})();
let packSearchT;
function debouncedPackSearch(){ clearTimeout(packSearchT); packSearchT = setTimeout(doPackSearch, 350); }
document.getElementById('packQuery').addEventListener('input', debouncedPackSearch);
document.getElementById('packMc').addEventListener('input', debouncedPackSearch);
document.getElementById('packSort').addEventListener('change', doPackSearch);
enhanceSelect(document.getElementById('packSort'));
wireChips('packFilters', f => { packLoader = f; doPackSearch(); });
document.getElementById('packImport').onclick = () => send({ type: 'importPack' });

// ── installed modpacks management (Packs screen) ──
// feature modules (WebUI/features/*.js) add buttons to each installed-pack row:
// registerPackAction({ key, label, title, onClick(pack, button) }) then send({type:'loadInstalledPacks'})
const packRowActions = [];
function registerPackAction(a){ if (!packRowActions.some(x => x.key === a.key)) packRowActions.push(a); }

function renderMyPacks(items){
  installedPacksCache = items || [];
  const box = document.getElementById('myPacksList');
  const cnt = document.getElementById('myPacksCount');
  cnt.textContent = items && items.length ? items.length : '';
  if (!items || !items.length){
    box.innerHTML = '<div class="empty">Пока нет установленных сборок — выбери в каталоге ниже</div>'; return;
  }
  box.innerHTML = items.map(p => `
    <div class="mp-row glass" style="background:var(--glass2)" data-slug="${esc(p.slug)}">
      <div class="mp-ico"${p.icon ? ` style="background-image:url('${esc(p.icon)}')"` : ''}></div>
      <div class="grow"><b>${esc(p.title)}${p.settings && p.settings.fps ? ' <span class="badge b-update" title="Буст FPS включён">⚡</span>' : ''}</b><small><span class="mp-badge">${esc(p.loader)}</span> ${esc(p.mc)}${p.version ? ' · ' + esc(p.version) : ''}${p.installed ? ' · ' + esc(p.installed) : ''}</small></div>
      <div class="mp-actions">
        <button class="btn primary" data-a="play">▶ Играть</button>
        <button class="btn" data-a="folder">Папка</button>
        <button class="btn" data-a="mods">Моды</button>
        <button class="btn" data-a="export" title="Экспорт в .mrpack">Экспорт</button>
        <button class="btn" data-a="doctor" title="Crash Doctor — разбор последнего краша">🩺</button>
        <button class="btn" data-a="time" title="Профили и машина времени">⏳${p.profiles && p.profiles.length ? ' ' + p.profiles.length : ''}</button>
        <button class="btn" data-a="settings" title="RAM / Java / JVM / окно / буст FPS">⚙</button>
        ${packRowActions.map(a => `<button class="btn" data-x="${esc(a.key)}" title="${esc(a.title || '')}">${a.label}</button>`).join('')}
        <button class="btn" data-a="reinstall" title="Докачать/обновить">Починить</button>
        <button class="btn danger" data-a="remove">Удалить</button>
      </div>
    </div>`).join('');
  box.querySelectorAll('.mp-row').forEach(el => {
    const slug = el.dataset.slug;
    const pack = items.find(p => p.slug === slug);
    el.querySelector('[data-a=settings]').onclick = () => PackSettings.open(pack);
    el.querySelector('[data-a=doctor]').onclick = () => send({ type: 'diagnosePack', slug });
    el.querySelector('[data-a=time]').onclick = () => TimeMachine.open(slug, pack && pack.title);
    el.querySelector('[data-a=export]').onclick = () => {
      JobPanel.update('export:' + slug, pack ? pack.title : slug, 'Экспорт .mrpack…', 0, 0);
      send({ type: 'exportPack', slug });
    };
    el.querySelector('[data-a=play]').onclick = () => launchPack(slug, pack && pack.title);
    el.querySelector('[data-a=folder]').onclick = () => send({ type: 'openPackFolder', slug });
    el.querySelector('[data-a=mods]').onclick = () => ModsModal.open(slug, pack && pack.title);
    el.querySelector('[data-a=reinstall]').onclick = () =>
      Dialog.confirm('Проверить сборку и докачать недостающие файлы? Перед этим сохранится снимок для отката.', 'Починить сборку', { okText: 'Починить' })
        .then(ok => { if (ok) send({ type: 'reinstallPack', slug }); });
    el.querySelector('[data-a=remove]').onclick = () =>
      Dialog.confirm('Удалить сборку и её файлы (в корзину)?', 'Удаление сборки', { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type: 'removePack', slug }); });
    packRowActions.forEach(a => {
      const b = el.querySelector(`[data-x="${a.key}"]`);
      if (b) b.onclick = () => a.onClick(pack, b);
    });
  });
  // feature modules decorate rows (.mp-row[data-slug]) after every render
  document.dispatchEvent(new CustomEvent('packs:rendered', { detail: { where: 'packs', items } }));
}

let packsData = [];
function renderPacks(items, error){
  packsData = items || [];
  const list = document.getElementById('packsList');
  if (error){
    // a failed request is not an empty catalog
    list.innerHTML = '<div class="empty" style="grid-column:1/-1">Нет соединения с Modrinth. Проверь интернет (или VPN/антивирус) и попробуй ещё раз.'
      + '<br><button class="btn" id="packsRetry">Повторить</button></div>';
    document.getElementById('packsRetry').onclick = doPackSearch;
    return;
  }
  if (!packsData.length){ list.innerHTML = '<div class="empty" style="grid-column:1/-1">Ничего не найдено — попробуй другой запрос или сними фильтры</div>'; return; }
  list.innerHTML = packsData.map((p, i) => `
    <div class="pack glass" style="background:var(--glass2)" data-i="${i}">
      <div class="pack-img" style="background-image:url('${esc(p.banner)}')"><span class="tag">${esc(p.loader)}</span></div>
      <div class="pack-body">
        <b>${esc(p.title)}</b>
        <p>${esc(p.description)}</p>
        <div class="pack-meta"><span>${esc(p.gameVersion)}</span><span>▼ ${esc(p.downloads)}</span></div>
      </div>
      <div style="display:flex;gap:8px;margin:0 14px 14px">
        <button class="btn primary" data-act="install" style="flex:1;margin:0">${p.ptype === 'modpack' || !p.ptype ? 'Установить' : 'В сборку…'}</button>
        <button class="btn" data-act="page" title="Открыть на Modrinth" style="margin:0">↗</button>
      </div>
    </div>`).join('');
  list.querySelectorAll('.pack').forEach(el => {
    const p = packsData[+el.dataset.i];
    el.querySelector('[data-act=install]').onclick = () =>
      (p.ptype === 'modpack' || !p.ptype) ? PackModal.open(p) : InstallTo.open(p);
    el.querySelector('[data-act=page]').onclick = () => send({ type: 'openPack', slug: p.slug, ptype: p.ptype || 'modpack' });
  });
}

// ── ACCOUNTS screen ──
function renderAccounts(list){
  const box = document.getElementById('accList');
  box.innerHTML = (list || []).map(a => `
    <div class="acc-row glass" style="background:var(--glass2)">
      <div class="skin${a.type === 'lic' ? '' : ' alt'}"></div>
      <div class="grow"><b>${esc(a.name)}</b><small>${esc(a.sub)}</small></div>
      <span class="acc-badge ${a.type === 'lic' ? 'lic' : 'off'}">${a.type === 'lic' ? 'лицензия' : 'оффлайн'}</span>
      ${a.active ? '<span class="acc-active">активен</span>'
                 : `<button class="btn" data-act="activate" data-id="${esc(a.id)}">Сделать активным</button>`}
      <button class="btn danger" data-act="remove" data-id="${esc(a.id)}">Убрать</button>
    </div>`).join('') || '<div class="empty">Нет аккаунтов — добавь ниже</div>';
  box.querySelectorAll('[data-act]').forEach(b => b.onclick = () => {
    const id = b.dataset.id;
    if (b.dataset.act === 'activate'){ send({ type: 'setActive', id }); return; }
    const a = (list || []).find(x => x.id === id);
    const name = a ? a.name : '';
    const tail = a && a.type === 'lic'
      ? ' Чтобы вернуть его, нужно будет снова войти через Microsoft.'
      : ' Миры, сборки и скины останутся на месте.';
    Dialog.confirm(`Убрать профиль «${name}» из лаунчера?${tail}`, 'Удаление профиля', { danger: true, okText: 'Убрать' })
      .then(ok => { if (ok) send({ type: 'removeAccount', id }); });
  });
  // a profile just added from the nick field → confirm and clear the field
  if (pendingNick && (list || []).some(a => a.name === pendingNick)){
    const input = document.getElementById('offNick');
    if (input && input.value.trim() === pendingNick) input.value = '';
    Toast.show(`Профиль «${pendingNick}» добавлен${Onboard.hasAccount === false ? ' — можно играть!' : ''}`);
    pendingNick = '';
  }
  Onboard.hasAccount = (list || []).length > 0;
  updateOnboarding();
}
let pendingNick = '';
document.getElementById('msLogin').onclick = () => send({ type: 'msLogin' });
// Minecraft accepts 3–16 latin letters, digits and "_" — anything else breaks servers and newer versions
const NICK_RE = /^[A-Za-z0-9_]{3,16}$/;
const TRANSLIT = { а:'a',б:'b',в:'v',г:'g',д:'d',е:'e',ё:'e',ж:'zh',з:'z',и:'i',й:'y',к:'k',л:'l',м:'m',н:'n',о:'o',п:'p',
  р:'r',с:'s',т:'t',у:'u',ф:'f',х:'h',ц:'c',ч:'ch',ш:'sh',щ:'sch',ъ:'',ы:'y',ь:'',э:'e',ю:'yu',я:'ya',і:'i',ї:'yi',є:'ye',ґ:'g' };
function suggestNick(s){
  const t = [...s].map(ch => { const l = ch.toLowerCase(), r = TRANSLIT[l];
    return r === undefined ? ch : (ch !== l && r ? r[0].toUpperCase() + r.slice(1) : r); }).join('');
  return t.replace(/\s+/g, '_').replace(/[^A-Za-z0-9_]/g, '').slice(0, 16);
}
document.getElementById('addOffline').onclick = () => {
  const input = document.getElementById('offNick');
  const nick = input.value.trim();
  if (!NICK_RE.test(nick)){
    const sug = suggestNick(nick);
    const hint = NICK_RE.test(sug) ? ` Например: «${sug}».` : '';
    Dialog.alert(`Ник должен быть от 3 до 16 символов: латинские буквы, цифры и «_», без пробелов.${hint}`, 'Неподходящий ник');
    if (NICK_RE.test(sug)) input.value = sug;
    return;
  }
  pendingNick = nick;
  send({ type: 'addOffline', nick });
  send({ type: 'loadSkins' }); // refresh skin profile list with the new account
};
document.getElementById('offNick').addEventListener('keydown', e => {
  if (e.key === 'Enter'){ e.preventDefault(); document.getElementById('addOffline').click(); }
});

// ── MODS (updates, folded into Packs) ──
function renderMods(items, error){
  const box = document.getElementById('modsList');
  const cnt = document.getElementById('modsCount');
  if (error){
    // {type:'mods', error} — the check failed; that's not "no mods" and not "not on Modrinth"
    cnt.textContent = '—';
    const h = humanizeError(error);
    box.innerHTML = `<div class="empty" title="${esc(h.details)}">Не удалось проверить моды: ${esc(h.text)}<br><button class="btn" id="modsRetry">Повторить</button></div>`;
    document.getElementById('modsRetry').onclick = () => document.getElementById('modsRefresh').click();
    return;
  }
  if (!items) return;
  const upd = items.filter(m => m.hasUpdate).length;
  cnt.textContent = !items.length ? 'нет' : (upd ? upd + ' обновл.' : items.length + ' модов');
  if (!items.length){ box.innerHTML = '<div class="empty">В папке mods нет .jar-файлов</div>'; return; }
  box.innerHTML = items.map(m => `
    <div class="mrow glass" style="background:var(--glass2)">
      <div class="micon">🧩</div>
      <div class="grow"><b>${esc(m.name)}</b>
        <small>${m.known
          ? esc(m.current || '—') + (m.hasUpdate ? ' → ' + esc(m.latest) : '')
          : esc(m.file) + ' · нет на Modrinth'}</small></div>
      ${m.hasUpdate
        ? `<span class="badge b-update">обновление</span><button class="btn primary" data-upd="${esc(m.hash)}" data-file="${esc(m.file)}">Обновить</button>`
        : (m.known ? '<span class="badge">актуально</span>' : '')}
    </div>`).join('');
  box.querySelectorAll('[data-upd]').forEach(b => b.onclick = () => {
    b.textContent = 'Обновление…'; b.disabled = true;
    send({ type: 'updateMod', hash: b.dataset.upd, file: b.dataset.file });
  });
}
document.getElementById('modsRefresh').onclick = () => {
  modsLoaded = true;
  document.getElementById('modsList').innerHTML = '<div class="empty">Проверяю моды на Modrinth…</div>';
  send({ type: 'loadMods' });
};
document.getElementById('modsFolder').onclick = () => send({ type: 'openModsFolder' });

// ── SKINS (offline profiles, folded into Accounts) ──
let skinAccounts = [], skinLibrary = [], skinSelId = '';
function faceCss(el, url, box){
  const scale = box / 8, size = 64 * scale, pos = 8 * scale;
  el.style.backgroundImage = `url('${url}')`;
  el.style.backgroundSize = size + 'px ' + size + 'px';
  el.style.backgroundPosition = '-' + pos + 'px -' + pos + 'px';
  el.style.backgroundRepeat = 'no-repeat';
}
const skinAccSel = document.getElementById('skinAccSel');
enhanceSelect(skinAccSel);
skinAccSel.addEventListener('change', () => { skinSelId = skinAccSel.value; renderSkinLib(); });
function curSkinAcc(){ return skinAccounts.find(a => a.id === skinSelId); }
function renderSkins(m){
  skinAccounts = (m.accounts || []).filter(a => a.offline);
  skinLibrary = m.library || [];
  if (!skinAccounts.length){ skinSelId = ''; skinAccSel.innerHTML = '<option>нет оффлайн-профилей</option>'; }
  else {
    if (!skinAccounts.find(a => a.id === skinSelId)) skinSelId = skinAccounts[0].id;
    skinAccSel.innerHTML = skinAccounts.map(a =>
      `<option value="${esc(a.id)}"${a.id === skinSelId ? ' selected' : ''}>${esc(a.name)}</option>`).join('');
  }
  if (skinAccSel._render) skinAccSel._render();
  renderSkinLib();
}
function renderSkinLib(){
  const acc = curSkinAcc();
  document.getElementById('skinAccName').textContent = acc ? acc.name : '—';
  document.getElementById('skinAccHint').textContent =
    acc ? (acc.skin ? 'скин назначен' : 'скин не выбран') : 'нет оффлайн-профиля';
  const face = document.getElementById('skinFace');
  face.style.backgroundImage = '';
  if (acc && acc.skin) faceCss(face, acc.skin, 96);
  const curFile = acc && acc.skin ? decodeURIComponent(acc.skin.split('/').pop()) : '';
  const lib = document.getElementById('skinLib');
  lib.innerHTML = skinLibrary.map(s =>
    `<div class="skin-card${s.file === curFile ? ' cur' : ''}" data-file="${esc(s.file)}">
       <div class="mini" data-face="${esc(s.url)}"></div><b>${esc(s.file)}</b></div>`).join('')
    + `<div class="skin-card upload" id="skinUpload">＋ Загрузить PNG<br>64×64</div>`;
  lib.querySelectorAll('.mini[data-face]').forEach(el => faceCss(el, el.dataset.face, 46));
  lib.querySelectorAll('.skin-card[data-file]').forEach(c => c.onclick = () => {
    if (!curSkinAcc()) return;
    const off = c.classList.contains('cur'); // clicking the current one clears it
    send({ type: 'assignSkin', id: skinSelId, file: off ? '' : c.dataset.file });
  });
  document.getElementById('skinUpload').onclick = () => send({ type: 'uploadSkin' });
}

// ── WORLDS & BACKUPS ──
let worldsData = [];
function renderWorlds(items){
  worldsData = items || [];
  const box = document.getElementById('worldsList');
  if (!worldsData.length){ box.innerHTML = '<div class="empty">Миров пока нет</div>'; return; }
  box.innerHTML = worldsData.map((w, i) => `
    <div class="wrow glass" style="background:var(--glass2)">
      <div class="wicon"></div>
      <div class="grow"><b>${esc(w.world || w.name)}${w.pack ? ` <span class="mp-badge" title="Мир из сборки">${esc(w.packTitle || w.pack)}</span>` : ''}</b><small>${esc(w.size)} · изменён ${esc(w.modified)}</small></div>
      <span class="badge">${w.backups.length} копий</span>
      <button class="btn" data-bk="${i}">Бэкап</button>
      ${w.backups.length ? `<button class="btn" data-hist="${i}">История</button>` : ''}
      <button class="btn" data-open="${i}">Папка</button>
    </div>
    <div class="wbackups glass" id="wb-${i}" style="display:none">
      ${w.backups.map(b => `<div class="tl-item"><span class="tl-dot"></span>${esc(b.date)} <small>· ${esc(b.size)}</small>
        <button class="btn" data-restore="${i}" data-file="${esc(b.file)}">Восстановить</button></div>`).join('')
        || '<div class="tl-item"><small>Пока нет копий</small></div>'}
    </div>`).join('');
  box.querySelectorAll('[data-bk]').forEach(b => b.onclick = () => {
    b.textContent = '…'; b.disabled = true; send({ type: 'backupWorld', name: worldsData[+b.dataset.bk].name });
  });
  box.querySelectorAll('[data-hist]').forEach(b => b.onclick = () => {
    const el = document.getElementById('wb-' + b.dataset.hist);
    el.style.display = el.style.display === 'none' ? 'block' : 'none';
  });
  box.querySelectorAll('[data-open]').forEach(b => b.onclick = () =>
    send({ type: 'openWorldFolder', name: worldsData[+b.dataset.open].name }));
  box.querySelectorAll('[data-restore]').forEach(b => b.onclick = () =>
    Dialog.confirm('Вернуть мир к этой копии? Текущее состояние сначала сохранится отдельной копией «перед откатом».', 'Откат мира', { okText: 'Откатить' })
      .then(ok => {
        if (!ok) return;
        b.textContent = '…'; b.disabled = true;
        send({ type: 'restoreWorld', name: worldsData[+b.dataset.restore].name, backup: b.dataset.file });
      }));
}
document.getElementById('worldsRefresh').onclick = () => {
  document.getElementById('worldsList').innerHTML = '<div class="empty">Загрузка миров…</div>';
  send({ type: 'loadWorlds' });
};
document.getElementById('savesFolder').onclick = () => send({ type: 'openSavesFolder' });

// ── SCREENSHOTS ──
// shotAll = every shot from every album (newest first); shotItems = the visible album
// (the Viewer's prev/next walks shotItems). `file` is a path relative to .minecraft.
let shotItems = [];
let shotAll = [];
let shotAlbumMeta = null;          // [{id,title,total}] from the last 'screens' message
let shotAlbumSel = null;           // null = «Все», '' = main game, otherwise pack slug
let shotPerAlbum = 0;

// albums from C# when available; otherwise derived from the items + installed packs
function shotAlbumList(){
  if (shotAlbumMeta) return shotAlbumMeta;
  const list = [{ id:'', title:'Основная игра', total:0 }];
  (installedPacksCache || []).forEach(p => list.push({ id:p.slug, title:p.title || p.slug, total:0 }));
  shotAll.forEach(s => {
    let a = list.find(x => x.id === s.album);
    if (!a){ a = { id:s.album, title:s.albumTitle || s.album, total:0 }; list.push(a); }
    a.total++;
  });
  return list;
}
function renderShotAlbums(){
  const bar = document.getElementById('shotAlbums');
  const albums = shotAlbumList();
  if (shotAlbumSel !== null && !albums.some(a => a.id === shotAlbumSel)) shotAlbumSel = null;
  const total = albums.reduce((n, a) => n + (a.total || 0), 0);
  const chip = (id, title, n, extraTitle) => `
    <button class="chip${shotAlbumSel === id ? ' on' : ''}${!n && id !== null ? ' empty-album' : ''}" data-a="${id === null ? '*' : esc(id)}"
      title="${esc(title)}${extraTitle ? ' · ' + esc(extraTitle) : ''}"><span class="t">${esc(title)}</span><span class="n">${n}</span></button>`;
  bar.innerHTML = chip(null, 'Все', total)
    + albums.map(a => chip(a.id, a.id === '' ? 'Основная игра' : a.title, a.total || 0,
        shotPerAlbum && a.total > shotPerAlbum ? `показаны последние ${shotPerAlbum}` : '')).join('');
  bar.querySelectorAll('.chip').forEach(b => b.onclick = () => {
    shotAlbumSel = b.dataset.a === '*' ? null : b.dataset.a;
    renderShotAlbums(); renderShotGrid();
  });
}
function renderShotGrid(){
  shotItems = shotAlbumSel === null ? shotAll : shotAll.filter(s => s.album === shotAlbumSel);
  const box = document.getElementById('shotsList');
  if (!shotItems.length){
    box.innerHTML = shotAlbumSel === null
      ? '<div class="empty">Скриншотов пока нет — нажми F2 в игре</div>'
      : '<div class="empty">В этом альбоме пусто</div>';
  } else {
    box.innerHTML = shotItems.map(s => `
      <div class="shot" data-file="${esc(s.file)}" style="background-image:url('${esc(s.url)}')">
        <span class="shot-album${s.album ? '' : ' main'}">${esc(s.albumTitle || 'Основная игра')}</span>
        <div class="meta">${esc(s.date)} · ${esc(s.size)}</div>
      </div>`).join('');
    box.querySelectorAll('.shot').forEach(el => el.onclick = () => Viewer.open(el.dataset.file));
  }
  // if the viewer is open (e.g. after a save/delete refresh) keep it in sync
  if (Viewer.isOpen()) Viewer.onListRefreshed();
}
function renderShots(items){
  shotAll = items || [];
  renderShotAlbums();
  renderShotGrid();
}
// extra payload of our own messages; registered before the main dispatcher,
// so the album meta is in place by the time it calls renderShots(m.items)
if (host) host.addEventListener('message', e => {
  const m = e.data;
  if (!m || !m.type) return;
  if (m.type === 'screens'){
    shotAlbumMeta = Array.isArray(m.albums) ? m.albums : null;
    shotPerAlbum = m.perAlbum || 0;
  }
  else if (m.type === 'shotSaved') Viewer.onSaved(m);
  else if (m.type === 'shotCover') Viewer.onCover(m);
});
document.getElementById('screensRefresh').onclick = () => {
  document.getElementById('shotsList').innerHTML = '<div class="empty">Загрузка…</div>';
  send({ type: 'loadScreens' });
};
document.getElementById('screensFolder').onclick = () =>
  send({ type: 'shotsOpenFolder', album: shotAlbumSel || '' });

// ── SCREENSHOT VIEWER + EDITOR (canvas) ──
const Viewer = (function(){
  const root   = document.getElementById('viewer');
  const canvas = document.getElementById('vwCanvas');
  const ctx    = canvas.getContext('2d');
  const wrap   = document.getElementById('vwWrap');
  const stage  = document.getElementById('vwStage');
  const cropBox= document.getElementById('vwCropBox');
  const hint   = document.getElementById('vwHint');
  const V = { file:'', index:-1, img:null, zoom:1, mode:'view', color:'#ff4d4d',
              undo:[], drawing:false, last:null, cropStart:null,
              edits:0, savedEdits:0, savingEdits:0 };   // edits ≠ savedEdits → unsaved changes on the canvas
  const dirty = () => !!V.img && V.edits !== V.savedEdits;
  // ask before throwing away edits (close / next / previous)
  function confirmDiscard(){
    if (!dirty()) return Promise.resolve(true);
    return Dialog.confirm('На снимке есть несохранённые правки. Закрыть его без сохранения?', 'Несохранённые правки',
      { danger: true, okText: 'Не сохранять', cancelText: 'Вернуться' });
  }
  function requestClose(){ confirmDiscard().then(ok => { if (ok) close(); }); }

  function isOpen(){ return !root.hasAttribute('hidden'); }
  function cur(){ return shotItems[V.index] || shotAll.find(s => s.file === V.file) || null; }
  function baseName(f){ return (f || '').split('/').pop(); }

  function open(file){
    V.file = file;
    V.index = shotItems.findIndex(s => s.file === file);
    V.mode = 'view'; setModeUI();
    root.removeAttribute('hidden');
    const it = cur();
    document.getElementById('vwName').textContent = it ? it.name : baseName(file);
    document.getElementById('vwAlbum').textContent = (it && it.albumTitle) || 'Основная игра';
    // a cover only makes sense for a pack instance
    document.querySelector('.vw-b[data-t=cover]').hidden = !(it && it.album);
    hint.textContent = 'Загрузка…';
    send({ type:'openShot', file });
    window.addEventListener('keydown', onKey);
  }
  function close(){
    root.setAttribute('hidden','');
    window.removeEventListener('keydown', onKey);
    V.img = null; V.undo = [];
  }
  function onData(file, dataUrl){
    if (file !== V.file) return;                 // ignore stale responses
    if (!dataUrl){
      // don't leave the previous picture (and its edits) on screen under the new name
      V.img = null; V.undo = []; V.edits = 0; V.savedEdits = 0;
      canvas.width = 0; canvas.height = 0;
      hint.textContent = 'Не удалось открыть файл — возможно, он удалён или повреждён';
      return;
    }
    const img = new Image();
    img.onload = () => { V.img = img; initFromImage(img); };
    img.src = dataUrl;
  }
  function initFromImage(img){
    canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
    ctx.drawImage(img, 0, 0);
    V.undo = []; V.edits = 0; V.savedEdits = 0;
    fit();
    hint.textContent = `${img.naturalWidth}×${img.naturalHeight} · колесо — зум · ← → листать`;
  }

  // ── zoom ──
  function setZoom(z){
    V.zoom = Math.min(8, Math.max(0.05, z));
    canvas.style.width  = (canvas.width  * V.zoom) + 'px';
    canvas.style.height = (canvas.height * V.zoom) + 'px';
    document.getElementById('vwZoom').textContent = Math.round(V.zoom * 100) + '%';
  }
  function fit(){
    const availW = stage.clientWidth - 48, availH = stage.clientHeight - 48;
    if (!canvas.width || !canvas.height) return;
    setZoom(Math.min(availW / canvas.width, availH / canvas.height, 8));
  }

  // ── undo ──
  function pushUndo(){
    try {
      V.undo.push({ w:canvas.width, h:canvas.height, data:ctx.getImageData(0,0,canvas.width,canvas.height) });
      if (V.undo.length > 12) V.undo.shift();
    } catch(e){}
    V.edits++;
  }
  function undo(){
    const s = V.undo.pop(); if (!s) return;
    V.edits--;
    canvas.width = s.w; canvas.height = s.h; ctx.putImageData(s.data, 0, 0);
    setZoom(V.zoom);
  }
  function reset(){ if (V.img){ V.undo = []; initFromImage(V.img); } }

  // ── rotate / crop ──
  function rotate(dir){
    pushUndo();
    const c = document.createElement('canvas'); c.width = canvas.height; c.height = canvas.width;
    const cx = c.getContext('2d');
    cx.translate(c.width/2, c.height/2);
    cx.rotate((dir === 'r' ? 90 : -90) * Math.PI/180);
    cx.drawImage(canvas, -canvas.width/2, -canvas.height/2);
    canvas.width = c.width; canvas.height = c.height; ctx.drawImage(c, 0, 0);
    fit();
  }
  function applyCrop(dx, dy, dw, dh){
    const sx = canvas.width / canvas.getBoundingClientRect().width;   // display→natural
    let x = Math.round(dx*sx), y = Math.round(dy*sx),
        w = Math.round(dw*sx), h = Math.round(dh*sx);
    x = Math.max(0, x); y = Math.max(0, y);
    w = Math.min(w, canvas.width - x); h = Math.min(h, canvas.height - y);
    if (w < 6 || h < 6) return;
    pushUndo();
    const c = document.createElement('canvas'); c.width = w; c.height = h;
    c.getContext('2d').drawImage(canvas, x, y, w, h, 0, 0, w, h);
    canvas.width = w; canvas.height = h; ctx.drawImage(c, 0, 0);
    setMode('view'); fit();
  }

  // ── pointer input (pen strokes + crop rectangle) ──
  function evtToDisp(e){ const r = canvas.getBoundingClientRect(); return { x:e.clientX - r.left, y:e.clientY - r.top }; }
  function evtToNat(e){ const r = canvas.getBoundingClientRect(); const s = canvas.width / r.width;
    return { x:(e.clientX - r.left)*s, y:(e.clientY - r.top)*s }; }

  canvas.addEventListener('pointerdown', e => {
    if (V.mode === 'pen'){
      pushUndo(); V.drawing = true; V.last = evtToNat(e);
      canvas.setPointerCapture(e.pointerId);
    } else if (V.mode === 'crop'){
      V.cropStart = evtToDisp(e); canvas.setPointerCapture(e.pointerId);
      cropBox.removeAttribute('hidden');
      cropBox.style.left = V.cropStart.x+'px'; cropBox.style.top = V.cropStart.y+'px';
      cropBox.style.width = '0px'; cropBox.style.height = '0px';
    }
  });
  canvas.addEventListener('pointermove', e => {
    if (V.mode === 'pen' && V.drawing){
      const p = evtToNat(e);
      ctx.strokeStyle = V.color; ctx.lineWidth = Math.max(2, Math.round(canvas.width/350));
      ctx.lineCap = 'round'; ctx.lineJoin = 'round';
      ctx.beginPath(); ctx.moveTo(V.last.x, V.last.y); ctx.lineTo(p.x, p.y); ctx.stroke();
      V.last = p;
    } else if (V.mode === 'crop' && V.cropStart){
      const p = evtToDisp(e);
      const x = Math.min(p.x, V.cropStart.x), y = Math.min(p.y, V.cropStart.y);
      cropBox.style.left = x+'px'; cropBox.style.top = y+'px';
      cropBox.style.width = Math.abs(p.x-V.cropStart.x)+'px'; cropBox.style.height = Math.abs(p.y-V.cropStart.y)+'px';
    }
  });
  function endPointer(e){
    if (V.mode === 'pen'){ V.drawing = false; }
    else if (V.mode === 'crop' && V.cropStart){
      const p = evtToDisp(e);
      const x = Math.min(p.x, V.cropStart.x), y = Math.min(p.y, V.cropStart.y);
      const w = Math.abs(p.x - V.cropStart.x), h = Math.abs(p.y - V.cropStart.y);
      V.cropStart = null; cropBox.setAttribute('hidden','');
      applyCrop(x, y, w, h);
    }
  }
  canvas.addEventListener('pointerup', endPointer);
  canvas.addEventListener('pointercancel', () => { V.drawing = false; V.cropStart = null; cropBox.setAttribute('hidden',''); });

  stage.addEventListener('wheel', e => {
    if (!isOpen()) return; e.preventDefault();
    setZoom(V.zoom * (e.deltaY < 0 ? 1.12 : 0.89));
  }, { passive:false });

  // ── mode + toolbar ──
  function setMode(m){ V.mode = (V.mode === m ? 'view' : m); setModeUI(); }
  function setModeUI(){
    wrap.classList.toggle('crop', V.mode === 'crop');
    wrap.classList.toggle('pen',  V.mode === 'pen');
    document.querySelector('.vw-b[data-t=crop]').classList.toggle('active', V.mode === 'crop');
    document.querySelector('.vw-b[data-t=pen]').classList.toggle('active', V.mode === 'pen');
    if (V.mode !== 'crop'){ cropBox.setAttribute('hidden',''); V.cropStart = null; }
  }
  function exportUrl(c){
    const jpeg = /\.jpe?g$/i.test(V.file);       // C# rejects a payload that doesn't match the extension
    return c.toDataURL(jpeg ? 'image/jpeg' : 'image/png', jpeg ? 0.95 : undefined);
  }
  function saveEdit(mode){
    if (!V.img) return;
    if (!dirty() && mode !== 'signed'){ hint.textContent = 'Правок нет — сохранять нечего'; return; }
    V.savingEdits = V.edits;
    send({ type:'saveShot', file:V.file, dataUrl:exportUrl(canvas), mode });
    hint.textContent = 'Сохраняю…';
  }
  // the original stays untouched unless the player explicitly asks to replace it
  function saveOverwrite(){
    if (!V.img) return;
    if (!dirty()){ hint.textContent = 'Правок нет — сохранять нечего'; return; }
    Dialog.confirm('Заменить исходный скриншот отредактированной версией? Оригинал будет перезаписан, вернуть его не получится. '
      + 'Чтобы оставить оригинал, нажми «Сохранить копию».', 'Заменить оригинал', { danger: true, okText: 'Заменить' })
      .then(ok => { if (ok) saveEdit('overwrite'); });
  }
  function onSaved(m){
    if (!isOpen() || m.file !== V.file) return;
    if (!m.ok){ hint.textContent = 'Не удалось сохранить'; return; }
    V.savedEdits = V.savingEdits;
    hint.textContent = m.mode === 'signed' ? 'Копия с подписью: ' + baseName(m.saved)
                     : m.mode === 'copy'   ? 'Копия сохранена: ' + baseName(m.saved)
                     : 'Сохранено ✓';
  }

  // ── «С подписью»: branded glass pill in the bottom-right, saved as a copy ──
  function pillPath(c, x, y, w, h){
    const r = h / 2;
    c.beginPath();
    c.moveTo(x + r, y); c.lineTo(x + w - r, y); c.arc(x + w - r, y + r, r, -Math.PI/2, Math.PI/2);
    c.lineTo(x + r, y + h); c.arc(x + r, y + r, r, Math.PI/2, Math.PI*1.5); c.closePath();
  }
  async function captioned(){
    const it = cur() || {};
    const day = (it.date || '').split(' ')[0] || new Date().toLocaleDateString('ru-RU');
    const text = ['Blockify', it.albumTitle || 'Основная игра', day].join(' · ');
    const out = document.createElement('canvas'); out.width = canvas.width; out.height = canvas.height;
    const x = out.getContext('2d'); x.drawImage(canvas, 0, 0);

    const fs = Math.max(12, Math.round(Math.min(out.width, out.height) * 0.022));
    const font = `600 ${fs}px "JetBrains Mono", monospace`;
    try { await document.fonts.load(font); } catch(e){}
    x.font = font;
    const padX = fs * 0.95, h = Math.round(fs * 2.15), dot = fs * 0.46, gap = fs * 0.6;
    const w = Math.round(x.measureText(text).width + padX * 2 + dot + gap);
    const m = Math.round(fs * 1.2);
    const bx = out.width - w - m, by = out.height - h - m;
    if (bx < 0 || by < 0) return out;               // image too small for a caption

    // soft drop shadow
    x.save(); x.shadowColor = 'rgba(0,0,0,.4)'; x.shadowBlur = fs; x.shadowOffsetY = fs * 0.2;
    pillPath(x, bx, by, w, h); x.fillStyle = 'rgba(0,0,0,.35)'; x.fill(); x.restore();
    // frosted backdrop: blur the pixels under the pill
    x.save(); pillPath(x, bx, by, w, h); x.clip();
    const pad = fs * 2;
    x.filter = `blur(${Math.round(fs * 0.7)}px)`;
    x.drawImage(canvas, bx - pad, by - pad, w + pad * 2, h + pad * 2, bx - pad, by - pad, w + pad * 2, h + pad * 2);
    x.filter = 'none';
    x.fillStyle = 'rgba(12,17,15,.5)'; x.fillRect(bx, by, w, h);
    const sheen = x.createLinearGradient(0, by, 0, by + h);
    sheen.addColorStop(0, 'rgba(255,255,255,.2)'); sheen.addColorStop(.55, 'rgba(255,255,255,0)');
    x.fillStyle = sheen; x.fillRect(bx, by, w, h);
    x.restore();
    // hairline rim
    x.save(); pillPath(x, bx + .5, by + .5, w - 1, h - 1);
    x.lineWidth = Math.max(1, fs / 14); x.strokeStyle = 'rgba(255,255,255,.28)'; x.stroke(); x.restore();
    // grass dot + text
    x.fillStyle = '#6BBF3B';
    x.beginPath(); x.arc(bx + padX + dot / 2, by + h / 2, dot / 2, 0, Math.PI * 2); x.fill();
    x.font = font; x.textBaseline = 'middle'; x.fillStyle = '#eef3ef';
    x.fillText(text, bx + padX + dot + gap, by + h / 2 + fs * 0.05);
    return out;
  }
  async function saveSigned(){
    if (!V.img) return;
    hint.textContent = 'Добавляю подпись…';
    const out = await captioned();
    V.savingEdits = V.edits;
    send({ type:'saveShot', file:V.file, dataUrl:exportUrl(out), mode:'signed' });
  }

  // ── «Сделать обложкой сборки» ──
  function makeCover(){
    const it = cur();
    if (!it || !it.album || !V.img) return;
    // unsaved edits (crop etc.) go along so the cover matches what's on screen
    const msg = { type:'shotSetCover', file:V.file };
    if (V.undo.length) msg.dataUrl = canvas.toDataURL('image/png');
    send(msg);
    hint.textContent = 'Ставлю обложку…';
  }
  function onCover(m){
    if (!isOpen()) return;
    hint.textContent = m.ok ? `Обложка «${m.title || m.album}» обновлена ✓` : 'Обложка не сохранена: ' + humanizeError(m.error).text;
  }

  function del(){
    const it = cur();
    Dialog.confirm('Удалить «' + (it ? it.name : baseName(V.file)) + '» в корзину?', 'Удаление скриншота', { danger: true, okText: 'Удалить' })
      .then(ok => { if (ok) send({ type:'deleteShot', file:V.file }); });
  }
  function nav(d){
    if (!shotItems.length) return;
    let i = V.index + d;
    if (i < 0) i = shotItems.length - 1;
    if (i >= shotItems.length) i = 0;
    const next = shotItems[i].file;
    confirmDiscard().then(ok => { if (ok) open(next); });
  }
  document.querySelector('.vw-tools').addEventListener('click', e => {
    const b = e.target.closest('.vw-b'); if (!b) return;
    switch (b.dataset.t){
      case 'prev': nav(-1); break;
      case 'next': nav(1); break;
      case 'zoomin': setZoom(V.zoom*1.15); break;
      case 'zoomout': setZoom(V.zoom*0.87); break;
      case 'fit': fit(); break;
      case 'rotl': rotate('l'); break;
      case 'rotr': rotate('r'); break;
      case 'crop': setMode('crop'); break;
      case 'pen': setMode('pen'); break;
      case 'undo': undo(); break;
      case 'reset': reset(); break;
      case 'save': saveOverwrite(); break;
      case 'copy': saveEdit('copy'); break;
      case 'sign': saveSigned(); break;
      case 'cover': makeCover(); break;
      case 'del': del(); break;
      case 'ext': send({ type:'openScreenshot', file:V.file }); break;
      case 'close': requestClose(); break;
    }
  });
  document.getElementById('vwColor').addEventListener('input', e => V.color = e.target.value);

  // the viewer covers the window title bar — let its top bar drag the window too
  document.querySelector('.vw-bar').addEventListener('mousedown', e => {
    if (e.button === 0 && !e.target.closest('.vw-b') && !e.target.closest('input')) send({ type:'drag' });
  });
  document.querySelector('.vw-bar').addEventListener('dblclick', e => {
    if (!e.target.closest('.vw-b') && !e.target.closest('input')) send({ type:'max' });
  });

  function onKey(e){
    if (e.key === 'Escape') requestClose();
    else if (e.key === 'ArrowLeft') nav(-1);
    else if (e.key === 'ArrowRight') nav(1);
    // e.code: Ctrl+Z works on the Russian layout too (e.key would be «я»)
    else if ((e.ctrlKey || e.metaKey) && e.code === 'KeyZ'){ e.preventDefault(); undo(); }
  }
  window.addEventListener('resize', () => { if (isOpen()) fit(); });

  function onListRefreshed(){
    const files = shotItems.map(s => s.file);
    const i = files.indexOf(V.file);
    if (i >= 0){ V.index = i; return; }          // still there → keep current canvas & edits
    if (!files.length){ close(); return; }        // last one deleted
    open(files[Math.min(Math.max(0, V.index), files.length - 1)]);
  }

  return { open, close, isOpen, onData, onListRefreshed, onSaved, onCover };
})();

// ── NEWS ──
function renderNews(items){
  const box = document.getElementById('newsList');
  if (!items || !items.length){ box.innerHTML = '<div class="empty">Новостей пока нет</div>'; return; }
  box.innerHTML = items.map(n => `
    <div class="news-item" data-link="${esc(n.link)}">
      <div class="news-thumb"${n.image ? ` style="background-image:url('${esc(n.image)}')"` : ''}></div>
      <div><b>${esc(n.title)}</b><span>${esc(n.date)}</span></div>
    </div>`).join('');
  box.querySelectorAll('.news-item').forEach(el => el.onclick = () => {
    if (el.dataset.link) send({ type: 'openLink', url: el.dataset.link });
  });
}

// ── SETTINGS ──
const ramRange = document.getElementById('ramRange');
// visible confirmation for every saved change (settings auto-save, but the user should see it)
let setStatusT;
function flashSaved(text){
  const st = document.getElementById('setStatus');
  st.textContent = text || 'Сохранено ✓'; st.classList.add('ok');
  clearTimeout(setStatusT);
  setStatusT = setTimeout(() => { st.textContent = 'Изменения сохраняются автоматически'; st.classList.remove('ok'); }, 2200);
}
function saveSetting(key, value){ send({ type: 'setting', key, value }); flashSaved(); }
ramRange.oninput = () => document.getElementById('ramVal').textContent = ramRange.value + ' ГБ';
ramRange.onchange = () => saveSetting('ram', +ramRange.value);
document.getElementById('favServer').onchange = e => saveSetting('favServer', e.target.value);
document.getElementById('mcDir').onchange = e => saveSetting('mcDir', e.target.value);
document.getElementById('javaPath').onchange = e => saveSetting('java', e.target.value.trim());
document.getElementById('javaBrowse').onclick = () => send({ type: 'browseJava' });
// «О программе»
document.getElementById('aboutLogs').onclick = () => send({ type: 'openLogs' });
document.getElementById('aboutLicenses').onclick = () => send({ type: 'openLicenses' });
document.getElementById('aboutIssue').onclick = () =>
  send({ type: 'openLink', url: 'https://github.com/Blockify-Launcher/Blockify-Launcher/issues' });
// language selector removed for v0.2: the UI is Russian-only (see G125)
['swJvm','swDiscord','swSnap','swClose'].forEach(id => {
  const el = document.getElementById(id);
  el.onchange = () => saveSetting(id, el.checked);
});

// ── HOME: installed modpacks strip ──
document.getElementById('hpAll').onclick = () => goTo('packs');
function renderInstalledPacks(items){
  const box = document.getElementById('hpRow');
  const wrap = document.getElementById('homePacks');
  const has = !!(items && items.length);
  document.getElementById('s-home').classList.toggle('has-packs', has);
  if (!has){ wrap.setAttribute('hidden',''); box.innerHTML = ''; return; }
  wrap.removeAttribute('hidden');
  box.innerHTML = items.map(p => `
    <div class="hp-card" data-slug="${esc(p.slug)}">
      <div class="hp-top"${p.icon ? ` style="background-image:linear-gradient(180deg,rgba(11,14,16,.15),rgba(11,14,16,.75)),url('${esc(p.icon)}')"` : ''}>
        <div class="hp-ico"${p.icon ? ` style="background-image:url('${esc(p.icon)}')"` : ''}></div>
      </div>
      <div class="hp-body">
        <b>${esc(p.title)}</b>
        <small>${esc(p.loader)} · ${esc(p.mc)}${p.version ? ' · ' + esc(p.version) : ''}</small>
        <div class="hp-actions">
          <button class="btn primary" data-act="play">▶ Играть</button>
          <button class="btn danger" data-act="rm" title="Удалить сборку">✕</button>
        </div>
      </div>
    </div>`).join('');
  box.querySelectorAll('.hp-card').forEach(el => {
    el.querySelector('[data-act=play]').onclick = () => {
      const p = items.find(x => x.slug === el.dataset.slug);
      launchPack(el.dataset.slug, p && p.title);
    };
    el.querySelector('[data-act=rm]').onclick = () =>
      Dialog.confirm('Удалить сборку и её файлы (в корзину)?', 'Удаление сборки', { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type:'removePack', slug: el.dataset.slug }); });
  });
  // feature modules decorate home cards (.hp-card[data-slug]) after every render
  document.dispatchEvent(new CustomEvent('packs:rendered', { detail: { where: 'home', items } }));
}

// ── background download / install panel ──
const JobPanel = (function(){
  const panel = document.getElementById('dlPanel');
  const list = document.getElementById('dlList');
  const jobs = new Map();  // id → {title, phase, cur, total, state}

  // the game-file downloader reports its file kinds / phases in English — show them in Russian
  const KIND = { runtime: 'Java', java: 'Java', library: 'библиотеки', libraries: 'библиотеки', native: 'нативные библиотеки',
    natives: 'нативные библиотеки', resource: 'ресурсы игры', resources: 'ресурсы игры', asset: 'ресурсы игры', assets: 'ресурсы игры',
    minecraft: 'клиент игры', client: 'клиент игры', jar: 'клиент игры', others: 'прочие файлы', other: 'прочие файлы',
    log: 'настройки журнала', logging: 'настройки журнала', mod: 'моды', mods: 'моды', version: 'версия игры' };
  const VERB = [
    [/^(downloading|download)\b/i, 'Скачиваю'], [/^(checking|check|verifying|verify|validating)\b/i, 'Проверяю'],
    [/^(extracting|unpacking|unzipping)\b/i, 'Распаковываю'], [/^(installing|install)\b/i, 'Устанавливаю'],
    [/^(preparing|initializing)\b/i, 'Готовлю'], [/^(launching|starting)\b/i, 'Запускаю'],
    [/^(copying)\b/i, 'Копирую'], [/^(patching|processing)\b/i, 'Обрабатываю'],
    [/^(done|completed?|finished)\b/i, 'Готово'],
  ];
  function phaseText(raw){
    const p = (raw == null ? '' : String(raw)).trim();
    if (!p || /[а-яё]/i.test(p)) return p;             // already Russian
    const k = KIND[p.toLowerCase()];
    if (k) return 'Загрузка: ' + k;
    for (const [re, ru] of VERB){
      if (!re.test(p)) continue;
      const rest = p.replace(re, '').replace(/^[\s:.\-…]+/, '').trim();
      return ru + (rest ? ' ' + (KIND[rest.toLowerCase()] || rest) : '') + (ru === 'Готово' ? '' : '…');
    }
    return p;
  }

  function paint(){
    if (!jobs.size){ panel.setAttribute('hidden',''); list.innerHTML = ''; return; }
    panel.removeAttribute('hidden');
    list.innerHTML = [...jobs.entries()].map(([id, j]) => {
      const pct = j.total > 0 ? Math.round(j.cur / j.total * 100) : 0;
      const indet = j.state === 'run' && j.total <= 0;
      const stateCls = j.state === 'ok' ? ' ok' : j.state === 'err' ? ' err' : '';
      const phase = phaseText(j.phase);
      const line = j.state === 'ok' ? 'Готово ✓'
                 : j.state === 'err' ? ('Ошибка: ' + esc(j.error ? j.error.text : 'что-то пошло не так'))
                 : j.total > 0 ? `${esc(phase)} · ${j.cur}/${j.total} (${pct}%)`
                 : esc(phase);
      const tip = j.state === 'err' && j.error && j.error.details ? ` title="${esc(j.error.details)}"` : '';
      const x = j.state === 'run' ? '' : `<button class="dl-x" data-x="${esc(id)}" title="Убрать">✕</button>`;
      return `<div class="dl-job${stateCls}" data-id="${esc(id)}">
        <b>${esc(j.title)}</b>${x}<small${tip}>${line}</small>
        <div class="dl-bar"><div class="dl-fill${indet ? ' indet' : ''}" style="width:${pct}%"></div></div>
      </div>`;
    }).join('');
  }
  function update(id, title, phase, cur, total){
    const j = jobs.get(id) || { state: 'run' };
    j.title = title || j.title || id; j.phase = phase || j.phase || '';
    j.cur = cur || 0; j.total = total || 0; j.state = 'run'; j.error = null;
    jobs.set(id, j); paint();
  }
  function finish(id, ok, error){
    const j = jobs.get(id); if (!j) return;
    j.state = ok ? 'ok' : 'err';
    j.error = ok ? null : humanizeError(error);
    paint();
    // success fades out by itself; an error stays until the player closes it (✕)
    if (ok) setTimeout(() => { if (jobs.get(id) === j && j.state === 'ok'){ jobs.delete(id); paint(); } }, 4000);
    else panel.classList.remove('min');
  }
  list.addEventListener('click', e => {
    const b = e.target.closest('.dl-x'); if (!b) return;
    jobs.delete(b.dataset.x); paint();
  });
  document.getElementById('dlMin').onclick = () => panel.classList.toggle('min');
  return { update, finish, phaseText };
})();

// ── modpack install modal (version picker; hands off to the background panel) ──
const PackModal = (function(){
  const root = document.getElementById('packModal');
  const body = document.getElementById('pmBody');
  let cur = null;

  function open(pack){
    cur = pack;
    document.getElementById('pmTitle').textContent = pack.title;
    body.innerHTML = '<div class="empty">Загрузка версий…</div>';
    root.removeAttribute('hidden');
    send({ type:'packVersions', slug: pack.slug, title: pack.title, icon: pack.icon || '' });
  }
  function close(){ root.setAttribute('hidden',''); cur = null; }

  function renderVersions(slug, items, error){
    if (!cur || cur.slug !== slug) return;
    if (error){
      const h = humanizeError(error);
      body.innerHTML = `<div class="empty" title="${esc(h.details)}">Не удалось загрузить версии: ${esc(h.text)}<br><button class="btn" id="pmRetry">Повторить</button></div>`;
      document.getElementById('pmRetry').onclick = () => { const p = cur; if (p) open(p); };
      return;
    }
    if (!items || !items.length){ body.innerHTML = '<div class="empty">Нет версий для установки</div>'; return; }
    body.innerHTML = items.slice(0, 40).map((v, i) => `
      <div class="pv-row${v.supported ? ' pv-pick' : ''}" data-i="${i}">
        <div class="grow" style="flex:1">
          <b>${esc(v.name || v.version)}</b>
          <small>${esc(v.gameVersion)} · <span class="pv-badge">${esc(v.loader)}</span></small>
        </div>
        ${v.supported
          ? `<button class="btn primary" data-inst="${i}">Установить</button>`
          : '<span class="pv-soon">загрузчик не поддерживается</span>'}
      </div>`).join('');
    const startInstall = i => {
      const v = items[i];
      // start a background job and get out of the way — launcher stays usable
      JobPanel.update(cur.slug, cur.title, 'Установка…', 0, 0);
      send({ type:'installPack', slug: cur.slug, title: cur.title, icon: cur.icon || '', versionId: v.id });
      close();
    };
    // whole supported row is clickable (not just the button)
    body.querySelectorAll('.pv-pick').forEach(row => row.onclick = () => startInstall(+row.dataset.i));
  }
  document.getElementById('pmClose').onclick = close;
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { open, close, renderVersions };
})();

// ── per-pack launch settings (RAM / Java / JVM / window) ──
const PackSettings = (function(){
  const root = document.getElementById('packSetModal');
  const ram = document.getElementById('psRam');
  let slug = '';
  const ramLabel = () => document.getElementById('psRamVal').textContent = +ram.value ? ram.value + ' ГБ' : 'глобально';
  ram.oninput = ramLabel;
  function open(pack){
    if (!pack) return;
    slug = pack.slug;
    const s = pack.settings || {};
    document.getElementById('psTitle').textContent = 'Настройки — ' + pack.title;
    ram.value = s.ram ? Math.round(s.ram / 1024) : 0; ramLabel();
    document.getElementById('psJava').value = s.java || '';
    document.getElementById('psJvm').value = s.jvm || '';
    document.getElementById('psW').value = s.w || '';
    document.getElementById('psH').value = s.h || '';
    document.getElementById('psFps').checked = !!s.fps;
    root.removeAttribute('hidden');
  }
  function close(){ root.setAttribute('hidden',''); slug = ''; }
  function save(){
    send({ type:'savePackSettings', slug, settings: {
      ram: (+ram.value || 0) * 1024,
      java: document.getElementById('psJava').value.trim(),
      jvm: document.getElementById('psJvm').value.trim(),
      w: parseInt(document.getElementById('psW').value, 10) || 0,
      h: parseInt(document.getElementById('psH').value, 10) || 0,
      fps: document.getElementById('psFps').checked
    }});
    close();
  }
  document.getElementById('psSave').onclick = save;
  document.getElementById('psReset').onclick = () => {
    ram.value = 0; ramLabel();
    ['psJava','psJvm','psW','psH'].forEach(id => document.getElementById(id).value = '');
  };
  document.getElementById('psClose').onclick = close;
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { open, close };
})();

// ── per-pack mod manager ──
const ModsModal = (function(){
  const root = document.getElementById('modsModal');
  const body = document.getElementById('mmBody');
  let slug = '';

  function open(packSlug, title){
    slug = packSlug;
    document.getElementById('mmTitle').textContent = 'Моды — ' + (title || packSlug);
    body.innerHTML = '<div class="empty">Сканирую моды и проверяю обновления…</div>';
    root.removeAttribute('hidden');
    send({ type:'loadPackMods', slug });
  }
  function close(){ root.setAttribute('hidden',''); slug = ''; }

  function render(forSlug, items, error){
    if (!slug || slug !== forSlug) return;
    if (error){
      const h = humanizeError(error);
      body.innerHTML = `<div class="empty" title="${esc(h.details)}">Не удалось загрузить список модов: ${esc(h.text)}<br><button class="btn" id="mmRetry">Повторить</button></div>`;
      document.getElementById('mmRetry').onclick = () => document.getElementById('mmRefresh').click();
      return;
    }
    if (!items || !items.length){ body.innerHTML = '<div class="empty">В сборке нет модов</div>'; return; }
    const upd = items.filter(m => m.hasUpdate).length;
    const off = items.filter(m => !m.enabled).length;
    body.innerHTML = `<div class="mm-count">${items.length} модов · ${upd ? upd + ' обновл. · ' : ''}${off ? off + ' выкл.' : 'все включены'}</div>`
      + items.map((m, i) => `
      <div class="mm-row${m.enabled ? '' : ' off'}" data-i="${i}">
        <label class="sw"><input type="checkbox" data-tgl="${i}"${m.enabled ? ' checked' : ''}><i></i></label>
        <div class="grow"><b>${esc(m.name)}</b>
          <small>${m.known
            ? esc(m.current || '—') + (m.hasUpdate ? ' → ' + esc(m.latest) : '') + ' · ' + esc(m.size)
            : esc(m.size) + ' · нет на Modrinth'}</small></div>
        ${m.hasUpdate ? `<button class="btn primary" data-upd="${i}">Обновить</button>` : ''}
        <button class="btn danger" data-del="${i}">✕</button>
      </div>`).join('');
    body.querySelectorAll('[data-tgl]').forEach(el => el.onchange = () =>
      send({ type:'togglePackMod', slug, file: items[+el.dataset.tgl].file }));
    body.querySelectorAll('[data-upd]').forEach(b => b.onclick = () => {
      b.textContent = '…'; b.disabled = true;
      const m = items[+b.dataset.upd];
      send({ type:'updatePackMod', slug, file: m.file, hash: m.hash });
    });
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = () => {
      const m = items[+b.dataset.del];
      Dialog.confirm('Удалить мод «' + m.name + '» в корзину?', 'Удаление мода', { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type:'deletePackMod', slug, file: m.file }); });
    });
  }
  document.getElementById('mmClose').onclick = close;
  document.getElementById('mmRefresh').onclick = () => {
    body.innerHTML = '<div class="empty">Сканирую моды и проверяю обновления…</div>';
    send({ type:'loadPackMods', slug });
  };
  document.getElementById('mmFolder').onclick = () => send({ type:'openPackMods', slug });
  root.addEventListener('click', e => { if (e.target === root) close(); });
  return { open, close, render };
})();

// ── receive from C# ──
if (host) host.addEventListener('message', e => {
  const m = e.data;
  if (!m || !m.type) return;
  switch (m.type){
    case 'init':
      if (m.verTag) document.getElementById('verTag').textContent = m.verTag;
      if (m.footVer) document.getElementById('footVer').textContent = m.footVer;
      if (m.version || m.verTag) document.getElementById('aboutVer').textContent = m.version ? 'v' + m.version : m.verTag;
      if (m.versions){ allVersions = m.versions; renderVersions('all'); }
      if (m.versionNames) fillVersionSelect(m.versionNames, m.selectedVersion);
      if (m.account) setAccountCard(m.account);
      if (m.accounts) renderAccounts(m.accounts);
      if (m.settings) applySettings(m.settings);
      if (m.javaList) fillJava(m.javaList, m.settings && m.settings.java);
      break;
    case 'news':    renderNews(m.items); break;
    case 'packs':
      if ((m.seq || 0) !== packSeq) break;   // an answer to an older search
      renderPacks(m.items, m.error);
      break;
    case 'versions': allVersions = m.items || []; renderVersions(currentVerFilter()); if (m.versionNames) fillVersionSelect(m.versionNames, m.selectedVersion); break;
    case 'accounts': renderAccounts(m.items); if (m.account) setAccountCard(m.account); break;
    case 'launchState': GameState.setBusy(m.busy); break;
    case 'gameState': GameState.set(m); break;
    case 'needAccount': askForAccount(); break;
    case 'needVersion': askForVersion(); break;
    case 'heroSub': document.getElementById('heroSub').textContent = m.text; break;
    case 'mods':    renderMods(m.items, m.error); break;
    case 'skins':   renderSkins(m); break;
    case 'worlds':  renderWorlds(m.items); break;
    case 'screens': renderShots(m.items); break;
    case 'shotData': Viewer.onData(m.file, m.dataUrl); break;
    case 'installedPacks': renderInstalledPacks(m.items); renderMyPacks(m.items); updateContinue(); break;
    case 'packVersions': PackModal.renderVersions(m.slug, m.items, m.error); break;
    case 'packMods': ModsModal.render(m.slug, m.items, m.error); break;
    case 'crashDoctor': CrashDoctor.show(m); break;
    case 'snapshots': TimeMachine.render(m.slug, m.items); break;
    case 'crashFixDone': CrashDoctor.fixDone(m); break;
    case 'jobProgress': JobPanel.update(m.id, m.title, m.phase, m.cur, m.total); break;
    case 'error': showError(m.message, m.title); break;
    case 'javaPath': document.getElementById('javaPath').value = m.path || ''; flashSaved(); break;
    case 'installDone':
      JobPanel.finish(m.id, m.ok, m.error);
      if (m.ok) send({ type: 'loadInstalledPacks' });
      // a failed version install must not leave its button stuck in «Установка…»
      else if (typeof m.id === 'string' && m.id.startsWith('ver:')) renderVersions(currentVerFilter());
      break;
  }
});

function currentVerFilter(){
  const on = document.querySelector('#verFilters .chip.on');
  return on ? on.dataset.f : 'all';
}
function setAccountCard(a){
  const none = !!a.none || a.type === 'none';
  document.getElementById('curName').textContent = none ? 'Нет профиля' : (a.name || '—');
  document.getElementById('curType').textContent = none ? 'добавь профиль' : a.type === 'lic' ? 'лицензия' : 'оффлайн';
  document.getElementById('accSkin').className = 'skin' + (a.type === 'lic' ? '' : ' alt');
  // the green dot means "ready to play" — not for the empty placeholder
  const dot = document.getElementById('accDot');
  if (dot){ dot.classList.toggle('off', none); dot.title = none ? 'Профиль не добавлен' : ''; }
  if (none){ Onboard.hasAccount = false; updateOnboarding(); }
}
function fillJava(list, sel){
  const v = sel && sel !== 'javaw.exe' ? sel : '';
  document.getElementById('javaPath').value = v;
}
function applySettings(s){
  if (s.ram != null){ ramRange.value = s.ram; document.getElementById('ramVal').textContent = s.ram + ' ГБ'; }
  { const ls = document.getElementById('langSel'); if (ls && s.lang){ ls.value = s.lang; if (ls._render) ls._render(); } }
  if (s.favServer != null) document.getElementById('favServer').value = s.favServer;
  if (s.mcDir != null) document.getElementById('mcDir').value = s.mcDir;
  document.getElementById('swJvm').checked = !!s.swJvm;
  document.getElementById('swDiscord').checked = !!s.swDiscord;
  document.getElementById('swSnap').checked = !!s.swSnap;
  if (s.swClose != null) document.getElementById('swClose').checked = !!s.swClose;
}

// tell C# we're ready for data
send({ type: 'ready' });
