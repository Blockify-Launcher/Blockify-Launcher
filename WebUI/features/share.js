'use strict';
// Feature: share an installed pack by code (BLK-…) + blockify://install/<code> deep links.
// C# side: MainWindow.Share.cs (shareMake → shareCode, shareResolve → shareResolved, shareReady).
(function(){
  if (typeof registerPackAction !== 'function' || !host) return;

  const style = document.createElement('style');
  style.textContent = `
.sh-code{font-family:var(--mono);font-size:21px;font-weight:600;letter-spacing:.06em;text-align:center;
  padding:16px 14px;border-radius:12px;border:1px solid var(--glass-line);background:rgba(0,0,0,.28);
  color:var(--text);user-select:all;cursor:text;overflow-wrap:anywhere;line-height:1.5}
.sh-row{display:flex;gap:8px;align-items:center}
.sh-row .btn{flex-shrink:0;margin:0}
.sh-input{flex:1;min-width:0;padding:9px 12px;border-radius:10px;border:1px solid var(--glass-line);
  background:rgba(0,0,0,.25);color:var(--text);font:12.5px var(--mono);outline:none}
.sh-input:focus{border-color:rgba(107,191,59,.5)}
.sh-input[readonly]{color:var(--muted)}
.sh-label{font-size:11.5px;color:var(--muted);margin-top:4px}
.sh-note{font-size:12px;color:var(--muted);line-height:1.5}
.sh-err{font-size:12.5px;color:var(--red);padding:10px 12px;border-radius:10px;
  border:1px solid rgba(224,86,74,.35);background:rgba(224,86,74,.08);line-height:1.45}
.sh-warn{font-size:12px;color:var(--gold);line-height:1.45}
.sh-card{display:flex;align-items:center;gap:14px;padding:13px;border-radius:12px;
  border:1px solid var(--glass-line);background:var(--glass2)}
.sh-card .mp-ico{width:52px;height:52px;flex-shrink:0}
.sh-card .grow{flex:1;min-width:0}
.sh-card .grow b{display:block;font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.sh-card .grow small{display:block;margin-top:4px;font-family:var(--mono);font-size:11px;color:var(--muted)}
.sh-actions{display:flex;gap:10px;justify-content:flex-end;margin-top:4px}
`;
  document.head.appendChild(style);

  function modal(id, title){
    const root = document.createElement('div');
    root.className = 'pmodal'; root.id = id; root.hidden = true;
    root.innerHTML = `
      <div class="pm-card glass" style="width:min(540px,100%)">
        <div class="pm-head"><b>${esc(title)}</b><button class="vw-b vw-close" title="Закрыть">✕</button></div>
        <div class="pm-sub"></div>
        <div class="pm-body"></div>
      </div>`;
    document.body.appendChild(root);
    return { root, sub: root.querySelector('.pm-sub'), body: root.querySelector('.pm-body'), close: root.querySelector('.vw-close') };
  }

  function selectNode(el){
    if (!el) return;
    if (typeof el.select === 'function'){ el.focus(); el.select(); return; }
    const r = document.createRange(); r.selectNodeContents(el);
    const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
  }
  // clipboard write must start inside the click handler; on refusal fall back to selecting the text
  function copyText(text, fallbackEl, btn){
    const orig = btn.dataset.label || (btn.dataset.label = btn.textContent);
    const flash = msg => { btn.textContent = msg; clearTimeout(btn._t); btn._t = setTimeout(() => { btn.textContent = orig; }, 2200); };
    const fail = () => { selectNode(fallbackEl); flash('Выделено — нажми Ctrl+C'); };
    try {
      navigator.clipboard.writeText(text).then(() => flash('Скопировано ✓'), fail);
    } catch (e) { fail(); }
  }

  const LOADERS = { fabric: 'Fabric', quilt: 'Quilt', forge: 'Forge', neoforge: 'NeoForge' };
  const loaderName = l => LOADERS[l] || l || '?';

  // ── outgoing: «🔗» on an installed pack row ──
  const Out = (function(){
    const m = modal('shareOutModal', '🔗 Поделиться сборкой');
    let slug = '', title = '';

    function open(pack){
      slug = pack.slug; title = pack.title || pack.slug;
      m.sub.textContent = title;
      m.body.innerHTML = '<div class="empty">Готовлю код…</div>';
      m.root.hidden = false;
      send({ type: 'shareMake', slug });
    }
    function close(){ m.root.hidden = true; slug = ''; }

    function render(r){
      if (m.root.hidden || r.slug !== slug) return;
      if (r.local){
        m.body.innerHTML = `
          <div class="sh-note">Эта сборка импортирована из файла или другого лаунчера — на Modrinth нет её версии,
            поэтому короткий код сделать нельзя. Отправь другу файл <b>.mrpack</b>: он откроет его через «⤓ Импорт…».</div>
          <div class="sh-actions"><button class="btn primary" data-a="export">Экспорт .mrpack…</button></div>`;
        m.body.querySelector('[data-a=export]').onclick = () => {
          JobPanel.update('export:' + slug, title, 'Экспорт .mrpack…', 0, 0);
          send({ type: 'exportPack', slug });
          close();
        };
        return;
      }
      if (!r.ok){
        m.body.innerHTML = `<div class="sh-err">${esc(r.error || 'Не удалось сделать код.')}</div>`;
        return;
      }
      m.body.innerHTML = `
        <div class="sh-code" data-r="code"></div>
        <div class="sh-actions" style="justify-content:center"><button class="btn primary" data-a="copy">Скопировать код</button></div>
        <div class="sh-label">Ссылка — откроет Blockify и сразу предложит установку:</div>
        <div class="sh-row">
          <input class="sh-input" data-r="link" type="text" readonly spellcheck="false">
          <button class="btn" data-a="copyLink">Копировать</button>
        </div>
        <div class="sh-note">Друг вставляет код в «🔗 По коду…» на вкладке «Сборки» — Blockify скачает с Modrinth ровно эту версию сборки.</div>`;
      const codeEl = m.body.querySelector('[data-r=code]');
      const linkEl = m.body.querySelector('[data-r=link]');
      codeEl.textContent = r.code;
      linkEl.value = r.link;
      m.body.querySelector('[data-a=copy]').onclick = e => copyText(r.code, codeEl, e.currentTarget);
      m.body.querySelector('[data-a=copyLink]').onclick = e => copyText(r.link, linkEl, e.currentTarget);
      linkEl.onclick = () => linkEl.select();
    }

    m.close.onclick = close;
    m.root.addEventListener('click', e => { if (e.target === m.root) close(); });
    return { open, close, render, root: m.root };
  })();

  // ── incoming: «🔗 По коду…» / blockify:// link ──
  const In = (function(){
    const m = modal('shareInModal', '🔗 Установка по коду');
    m.sub.textContent = 'Вставь код сборки (BLK-…) или ссылку blockify://install/… от друга.';
    m.body.innerHTML = `
      <div class="sh-row">
        <input class="sh-input" data-r="code" type="text" placeholder="BLK-XXXX-XXXX-XXX" spellcheck="false" autocomplete="off">
        <button class="btn primary" data-a="find">Найти</button>
      </div>
      <div data-r="out"></div>`;
    const input = m.body.querySelector('[data-r=code]');
    const out = m.body.querySelector('[data-r=out]');
    const findBtn = m.body.querySelector('[data-a=find]');
    let waiting = false;

    function open(prefill){
      if (typeof prefill === 'string') input.value = prefill;
      out.innerHTML = '';
      m.root.hidden = false;
      setTimeout(() => input.focus(), 0);
    }
    function close(){ m.root.hidden = true; waiting = false; }

    function resolve(){
      const code = input.value.trim();
      if (!code){ out.innerHTML = '<div class="sh-err">Вставь код или ссылку.</div>'; input.focus(); return; }
      waiting = true;
      out.innerHTML = '<div class="empty">Ищу сборку на Modrinth…</div>';
      send({ type: 'shareResolve', code });
    }

    function render(r){
      if (!r.ok){
        out.innerHTML = `<div class="sh-err">${esc(r.error || 'Код не распознан.')}</div>`;
        return;
      }
      const notes = [];
      if (!r.supported) notes.push(`Загрузчик «${esc(r.loader || '?')}» лаунчер пока не поддерживает — установка, скорее всего, не получится.`);
      if (r.sameVersion) notes.push('Эта версия уже установлена — установка проверит и докачает недостающие файлы.');
      else if (r.isInstalled) notes.push(`Сборка уже установлена${r.installedVersion ? ' (' + esc(r.installedVersion) + ')' : ''} — установка переведёт её на эту версию в той же папке.`);
      out.innerHTML = `
        <div class="sh-card">
          <div class="mp-ico"></div>
          <div class="grow">
            <b></b>
            <small><span class="mp-badge">${esc(loaderName(r.loader))}</span> ${esc(r.mc)}${r.versionNumber ? ' · ' + esc(r.versionNumber) : ''}</small>
          </div>
        </div>
        ${notes.map(n => `<div class="sh-warn">${n}</div>`).join('')}
        <div class="sh-actions">
          <button class="btn" data-a="cancel">Отмена</button>
          <button class="btn primary" data-a="install">${r.sameVersion ? 'Проверить и докачать' : 'Установить'}</button>
        </div>`;
      out.querySelector('.sh-card b').textContent = r.title || r.slug;
      if (/^https:\/\//i.test(r.icon || '')) out.querySelector('.sh-card .mp-ico').style.backgroundImage = 'url(' + JSON.stringify(r.icon) + ')';
      out.querySelector('[data-a=cancel]').onclick = close;
      out.querySelector('[data-a=install]').onclick = () => {
        JobPanel.update(r.slug, r.title || r.slug, 'Установка…', 0, 0);
        send({ type: 'installPack', slug: r.slug, title: r.title || r.slug, icon: r.icon || '', versionId: r.versionId });
        close();
      };
    }

    // id of the last link shown: C# reposts the newest link result after shareReady (show it once), while a
    // link clicked later — the launcher was already open — comes with a new id and opens again
    let lastAutoId = null;
    function onResolved(r){
      if (r.auto){
        const id = r.linkId ?? 0;
        if (id === lastAutoId) return;
        lastAutoId = id;
        open(r.code || r.input || '');
        render(r);
        return;
      }
      if (!waiting || m.root.hidden) return;
      waiting = false;
      render(r);
    }

    findBtn.onclick = resolve;
    input.addEventListener('keydown', e => { if (e.key === 'Enter'){ e.preventDefault(); resolve(); } });
    m.close.onclick = close;
    m.root.addEventListener('click', e => { if (e.target === m.root) close(); });
    return { open, close, onResolved, root: m.root };
  })();

  window.addEventListener('keydown', e => {
    if (e.key !== 'Escape') return;
    const dlg = document.getElementById('dlg');
    if (dlg && !dlg.hasAttribute('hidden')) return;   // the glass dialog handles its own Escape
    if (!In.root.hidden) In.close();
    else if (!Out.root.hidden) Out.close();
  }, true);   // capture: runs before Dialog's handler, so an open dialog is still visible here

  const imp = document.getElementById('packImport');
  if (imp){
    const b = document.createElement('button');
    b.className = 'btn'; b.id = 'packByCode';
    b.title = 'Установить сборку по коду или ссылке blockify://';
    b.textContent = '🔗 По коду…';
    b.onclick = () => In.open('');
    imp.insertAdjacentElement('afterend', b);
  }

  host.addEventListener('message', e => {
    const msg = e.data;
    if (!msg || typeof msg !== 'object') return;
    if (msg.type === 'shareCode') Out.render(msg);
    else if (msg.type === 'shareResolved') In.onResolved(msg);
  });

  registerPackAction({ key: 'share', label: '🔗', title: 'Поделиться кодом', onClick: pack => { if (pack) Out.open(pack); } });
  send({ type: 'loadInstalledPacks' });
  send({ type: 'shareReady' });
})();
