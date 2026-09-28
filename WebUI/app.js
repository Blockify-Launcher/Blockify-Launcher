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
const esc = s => (s ?? '').toString().replace(/[&<>"]/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' }[c]));

// ── unified glass dialog (replaces native confirm/alert; also shows C# errors) ──
const Dialog = (function(){
  const root = document.getElementById('dlg');
  const okBtn = document.getElementById('dlgOk');
  const cancelBtn = document.getElementById('dlgCancel');
  let resolver = null;
  function open(msg, title, opts){
    opts = opts || {};
    document.getElementById('dlgTitle').textContent = title || 'Blockify';
    document.getElementById('dlgMsg').textContent = msg || '';
    cancelBtn.style.display = opts.confirm ? '' : 'none';
    okBtn.textContent = opts.okText || 'ОК';
    cancelBtn.textContent = opts.cancelText || 'Отмена';
    okBtn.classList.toggle('danger', !!opts.danger);
    okBtn.classList.toggle('primary', !opts.danger);
    root.removeAttribute('hidden');
    okBtn.focus();
    return new Promise(res => { resolver = res; });
  }
  function done(val){ root.setAttribute('hidden',''); const r = resolver; resolver = null; if (r) r(val); }
  okBtn.onclick = () => done(true);
  cancelBtn.onclick = () => done(false);
  root.addEventListener('click', e => { if (e.target === root) done(false); });
  window.addEventListener('keydown', e => {
    if (root.hasAttribute('hidden')) return;
    if (e.key === 'Escape') done(false);
    else if (e.key === 'Enter') done(true);
  });
  return {
    alert: (msg, title) => open(msg, title, { confirm: false }),
    confirm: (msg, title, opts) => open(msg, title, Object.assign({ confirm: true }, opts || {}))
  };
})();

// ── HOME: play + version ──
document.getElementById('playBtn').onclick = () => send({ type: 'launch' });

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
      document.getElementById('verValue').textContent = versionSel;
      closeDrops();
      send({ type: 'selectVersion', name: versionSel });
    });
  });
});
function fillVersionSelect(names, selected){
  versionNames = names || [];
  versionSel = selected || versionNames[0] || '';
  document.getElementById('verValue').textContent = versionSel || '—';
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
function doPackSearch(){
  const q = document.getElementById('packQuery').value.trim();
  const mc = document.getElementById('packMc').value.trim();
  const sort = document.getElementById('packSort').value;
  document.getElementById('packsList').innerHTML = '<div class="empty">Поиск…</div>';
  send({ type: 'searchPacks', query: q, loader: packLoader, gameVersion: mc, sort, ptype: packType });
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
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = () => send({ type: 'deleteSnapshot', slug, id: b.dataset.del }));
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
    if (!r.found){ body.innerHTML = `<div class="empty">${esc(r.headline || 'Нет данных')}</div>`; }
    else body.innerHTML = `<div class="cd-head">${esc(r.headline)}</div>` + (r.items || []).map((it, i) => `
      <div class="cd-item">
        <b>${esc(it.title)}</b><p>${esc(it.detail)}</p>
        <div class="cd-fixes">${(it.fixes || []).map((f, j) =>
          `<button class="btn ${f.kind === 'disable' || f.kind === 'install' || f.kind === 'ram' ? 'primary' : ''}" data-i="${i}" data-j="${j}">${esc(f.label)}</button>`).join('')}</div>
      </div>`).join('');
    body.querySelectorAll('[data-i]').forEach(b => b.onclick = () => {
      const f = r.items[+b.dataset.i].fixes[+b.dataset.j];
      b.textContent = '…'; b.classList.add('done');
      send({ type: 'crashFix', slug, kind: f.kind, file: f.file || '', modSlug: f.slug || '', ram: f.ram || 0 });
      b.dataset.label = f.label;
    });
    root.removeAttribute('hidden');
  }
  function fixDone(m){
    if (m.slug !== slug) return;
    body.querySelectorAll('.btn.done').forEach(b => {
      if (b.textContent === '…'){ b.textContent = m.ok ? '✓ ' + (b.dataset.label || 'Готово') : '✕ ' + (m.error || 'ошибка'); }
    });
  }
  function close(){ root.setAttribute('hidden',''); slug = ''; }
  document.getElementById('cdClose').onclick = close;
  document.getElementById('cdLogs').onclick = () => send({ type: 'crashFix', slug, kind: 'openLogs' });
  document.getElementById('cdLaunch').onclick = () => { const s = slug; close(); send({ type: 'launchPack', slug: s }); };
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
    el.querySelector('[data-a=play]').onclick = () => send({ type: 'launchPack', slug });
    el.querySelector('[data-a=folder]').onclick = () => send({ type: 'openPackFolder', slug });
    el.querySelector('[data-a=mods]').onclick = () => ModsModal.open(slug, pack && pack.title);
    el.querySelector('[data-a=reinstall]').onclick = () => send({ type: 'reinstallPack', slug });
    el.querySelector('[data-a=remove]').onclick = () =>
      Dialog.confirm('Удалить сборку и её файлы (в корзину)?', 'Удаление сборки', { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type: 'removePack', slug }); });
  });
}

let packsData = [];
function renderPacks(items){
  packsData = items || [];
  const list = document.getElementById('packsList');
  if (!packsData.length){ list.innerHTML = '<div class="empty">Ничего не найдено</div>'; return; }
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
  box.querySelectorAll('[data-act]').forEach(b => b.onclick = () =>
    send({ type: b.dataset.act === 'activate' ? 'setActive' : 'removeAccount', id: b.dataset.id }));
}
document.getElementById('msLogin').onclick = () => send({ type: 'msLogin' });
document.getElementById('addOffline').onclick = () => {
  const nick = document.getElementById('offNick').value.trim();
  send({ type: 'addOffline', nick });
  send({ type: 'loadSkins' }); // refresh skin profile list with the new account
};

// ── MODS (updates, folded into Packs) ──
function renderMods(items){
  const box = document.getElementById('modsList');
  const cnt = document.getElementById('modsCount');
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
  if (!worldsData.length){ box.innerHTML = '<div class="empty">В папке saves нет миров</div>'; return; }
  box.innerHTML = worldsData.map((w, i) => `
    <div class="wrow glass" style="background:var(--glass2)">
      <div class="wicon"></div>
      <div class="grow"><b>${esc(w.name)}</b><small>${esc(w.size)} · изменён ${esc(w.modified)}</small></div>
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
  box.querySelectorAll('[data-restore]').forEach(b => b.onclick = () => {
    b.textContent = '…'; b.disabled = true;
    send({ type: 'restoreWorld', name: worldsData[+b.dataset.restore].name, backup: b.dataset.file });
  });
}
document.getElementById('worldsRefresh').onclick = () => {
  document.getElementById('worldsList').innerHTML = '<div class="empty">Загрузка миров…</div>';
  send({ type: 'loadWorlds' });
};
document.getElementById('savesFolder').onclick = () => send({ type: 'openSavesFolder' });

// ── SCREENSHOTS ──
let shotItems = [];
function renderShots(items){
  shotItems = items || [];
  const box = document.getElementById('shotsList');
  if (!shotItems.length){ box.innerHTML = '<div class="empty">В папке screenshots пусто</div>'; return; }
  box.innerHTML = shotItems.map(s => `
    <div class="shot" data-file="${esc(s.file)}" style="background-image:url('${esc(s.url)}')">
      <div class="meta">${esc(s.date)} · ${esc(s.size)}</div>
    </div>`).join('');
  box.querySelectorAll('.shot').forEach(el => el.onclick = () => Viewer.open(el.dataset.file));
  // if the viewer is open (e.g. after a save/delete refresh) keep it in sync
  if (Viewer.isOpen()) Viewer.onListRefreshed();
}
document.getElementById('screensRefresh').onclick = () => {
  document.getElementById('shotsList').innerHTML = '<div class="empty">Загрузка…</div>';
  send({ type: 'loadScreens' });
};
document.getElementById('screensFolder').onclick = () => send({ type: 'openScreensFolder' });

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
              undo:[], drawing:false, last:null, cropStart:null };

  function isOpen(){ return !root.hasAttribute('hidden'); }

  function open(file){
    V.file = file;
    V.index = shotItems.findIndex(s => s.file === file);
    V.mode = 'view'; setModeUI();
    root.removeAttribute('hidden');
    document.getElementById('vwName').textContent = file;
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
    if (!dataUrl){ hint.textContent = 'Не удалось открыть файл'; return; }
    const img = new Image();
    img.onload = () => { V.img = img; initFromImage(img); };
    img.src = dataUrl;
  }
  function initFromImage(img){
    canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
    ctx.drawImage(img, 0, 0);
    V.undo = [];
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
  }
  function undo(){
    const s = V.undo.pop(); if (!s) return;
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
  function saveEdit(mode){
    const jpeg = /\.jpe?g$/i.test(V.file);
    const url = canvas.toDataURL(jpeg ? 'image/jpeg' : 'image/png', jpeg ? 0.95 : undefined);
    send({ type:'saveShot', file:V.file, dataUrl:url, mode });
    hint.textContent = mode === 'copy' ? 'Копия сохранена в папку' : 'Сохранено ✓';
  }
  function del(){
    Dialog.confirm('Удалить «' + V.file + '» в корзину?', 'Удаление скриншота', { danger: true, okText: 'Удалить' })
      .then(ok => { if (ok) send({ type:'deleteShot', file:V.file }); });
  }
  function nav(d){
    if (!shotItems.length) return;
    let i = V.index + d;
    if (i < 0) i = shotItems.length - 1;
    if (i >= shotItems.length) i = 0;
    open(shotItems[i].file);
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
      case 'save': saveEdit('overwrite'); break;
      case 'copy': saveEdit('copy'); break;
      case 'del': del(); break;
      case 'ext': send({ type:'openScreenshot', file:V.file }); break;
      case 'close': close(); break;
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
    if (e.key === 'Escape') close();
    else if (e.key === 'ArrowLeft') nav(-1);
    else if (e.key === 'ArrowRight') nav(1);
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z'){ e.preventDefault(); undo(); }
  }
  window.addEventListener('resize', () => { if (isOpen()) fit(); });

  function onListRefreshed(){
    const files = shotItems.map(s => s.file);
    const i = files.indexOf(V.file);
    if (i >= 0){ V.index = i; return; }          // still there → keep current canvas & edits
    if (!files.length){ close(); return; }        // last one deleted
    open(files[Math.min(Math.max(0, V.index), files.length - 1)]);
  }

  return { open, close, isOpen, onData, onListRefreshed };
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
document.getElementById('langSel').addEventListener('change', e => saveSetting('lang', e.target.value));
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
    el.querySelector('[data-act=play]').onclick = () => send({ type:'launchPack', slug: el.dataset.slug });
    el.querySelector('[data-act=rm]').onclick = () =>
      Dialog.confirm('Удалить сборку и её файлы (в корзину)?', 'Удаление сборки', { danger: true, okText: 'Удалить' })
        .then(ok => { if (ok) send({ type:'removePack', slug: el.dataset.slug }); });
  });
}

// ── background download / install panel ──
const JobPanel = (function(){
  const panel = document.getElementById('dlPanel');
  const list = document.getElementById('dlList');
  const jobs = new Map();  // id → {title, phase, cur, total, state}

  function paint(){
    if (!jobs.size){ panel.setAttribute('hidden',''); list.innerHTML = ''; return; }
    panel.removeAttribute('hidden');
    list.innerHTML = [...jobs.entries()].map(([id, j]) => {
      const pct = j.total > 0 ? Math.round(j.cur / j.total * 100) : 0;
      const indet = j.state === 'run' && j.total <= 0;
      const stateCls = j.state === 'ok' ? ' ok' : j.state === 'err' ? ' err' : '';
      const line = j.state === 'ok' ? 'Готово ✓'
                 : j.state === 'err' ? ('Ошибка: ' + (j.error || ''))
                 : j.total > 0 ? `${esc(j.phase)} · ${j.cur}/${j.total} (${pct}%)`
                 : esc(j.phase);
      return `<div class="dl-job${stateCls}" data-id="${esc(id)}">
        <b>${esc(j.title)}</b><small>${line}</small>
        <div class="dl-bar"><div class="dl-fill${indet ? ' indet' : ''}" style="width:${pct}%"></div></div>
      </div>`;
    }).join('');
  }
  function update(id, title, phase, cur, total){
    const j = jobs.get(id) || { state: 'run' };
    j.title = title || j.title || id; j.phase = phase || j.phase || '';
    j.cur = cur || 0; j.total = total || 0; j.state = 'run';
    jobs.set(id, j); paint();
  }
  function finish(id, ok, error){
    const j = jobs.get(id); if (!j) return;
    j.state = ok ? 'ok' : 'err'; j.error = error; paint();
    // keep the finished row briefly, then drop it
    setTimeout(() => { jobs.delete(id); paint(); }, ok ? 3500 : 8000);
  }
  document.getElementById('dlMin').onclick = () => panel.classList.toggle('min');
  return { update, finish };
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
    if (error){ body.innerHTML = `<div class="empty">Ошибка: ${esc(error)}</div>`; return; }
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

  function render(forSlug, items){
    if (!slug || slug !== forSlug) return;
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
      if (m.versions){ allVersions = m.versions; renderVersions('all'); }
      if (m.versionNames) fillVersionSelect(m.versionNames, m.selectedVersion);
      if (m.account) setAccountCard(m.account);
      if (m.accounts) renderAccounts(m.accounts);
      if (m.settings) applySettings(m.settings);
      if (m.javaList) fillJava(m.javaList, m.settings && m.settings.java);
      break;
    case 'news':    renderNews(m.items); break;
    case 'packs':   renderPacks(m.items); break;
    case 'versions': allVersions = m.items || []; renderVersions(currentVerFilter()); if (m.versionNames) fillVersionSelect(m.versionNames, m.selectedVersion); break;
    case 'accounts': renderAccounts(m.items); if (m.account) setAccountCard(m.account); break;
    case 'launchState': setLaunchState(m.busy); break;
    case 'heroSub': document.getElementById('heroSub').textContent = m.text; break;
    case 'mods':    renderMods(m.items); break;
    case 'skins':   renderSkins(m); break;
    case 'worlds':  renderWorlds(m.items); break;
    case 'screens': renderShots(m.items); break;
    case 'shotData': Viewer.onData(m.file, m.dataUrl); break;
    case 'installedPacks': renderInstalledPacks(m.items); renderMyPacks(m.items); break;
    case 'packVersions': PackModal.renderVersions(m.slug, m.items, m.error); break;
    case 'packMods': ModsModal.render(m.slug, m.items); break;
    case 'crashDoctor': CrashDoctor.show(m); break;
    case 'snapshots': TimeMachine.render(m.slug, m.items); break;
    case 'crashFixDone': CrashDoctor.fixDone(m); break;
    case 'jobProgress': JobPanel.update(m.id, m.title, m.phase, m.cur, m.total); break;
    case 'error': Dialog.alert(m.message, 'Ошибка'); break;
    case 'javaPath': document.getElementById('javaPath').value = m.path || ''; flashSaved(); break;
    case 'installDone':
      JobPanel.finish(m.id, m.ok, m.error);
      if (m.ok) send({ type: 'loadInstalledPacks' });
      break;
  }
});

function currentVerFilter(){
  const on = document.querySelector('#verFilters .chip.on');
  return on ? on.dataset.f : 'all';
}
function setAccountCard(a){
  document.getElementById('curName').textContent = a.name || '—';
  document.getElementById('curType').textContent = a.type === 'lic' ? 'лицензия' : 'оффлайн';
  document.getElementById('accSkin').className = 'skin' + (a.type === 'lic' ? '' : ' alt');
}
function setLaunchState(busy){
  const b = document.getElementById('playBtn');
  b.textContent = busy ? 'ЗАПУСК…' : '▶  ИГРАТЬ';
  b.style.opacity = busy ? .7 : 1;
  b.style.pointerEvents = busy ? 'none' : 'auto';
}
function fillJava(list, sel){
  const v = sel && sel !== 'javaw.exe' ? sel : '';
  document.getElementById('javaPath').value = v;
}
function applySettings(s){
  if (s.ram != null){ ramRange.value = s.ram; document.getElementById('ramVal').textContent = s.ram + ' ГБ'; }
  if (s.lang){ const ls = document.getElementById('langSel'); ls.value = s.lang; if (ls._render) ls._render(); }
  if (s.favServer != null) document.getElementById('favServer').value = s.favServer;
  if (s.mcDir != null) document.getElementById('mcDir').value = s.mcDir;
  document.getElementById('swJvm').checked = !!s.swJvm;
  document.getElementById('swDiscord').checked = !!s.swDiscord;
  document.getElementById('swSnap').checked = !!s.swSnap;
  if (s.swClose != null) document.getElementById('swClose').checked = !!s.swClose;
}

// tell C# we're ready for data
send({ type: 'ready' });
