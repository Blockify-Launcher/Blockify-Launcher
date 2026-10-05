// Blockify — сайт-«срез мира». Все текстуры процедурные (canvas), без внешних картинок.
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const wait = ms => new Promise(r => setTimeout(r, reduced ? Math.min(ms, 60) : ms));

  // ── детерминированный ГПСЧ, чтобы текстуры не «прыгали» между загрузками ──
  function rng(seed) {
    return () => {
      seed |= 0; seed = seed + 0x6D2B79F5 | 0;
      let t = Math.imul(seed ^ seed >>> 15, 1 | seed);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
  }
  const pick = (r, a) => a[Math.floor(r() * a.length)];

  const PAL = {
    grass:   ['#6BBF3B', '#5fae33', '#77c947', '#4E9427'],
    dirt:    ['#866043', '#79553a', '#6b4a31', '#9b7653', '#5d4128'],
    stone:   ['#7f7f7f', '#747474', '#8a8a8a', '#6a6a6a', '#7a7a7a'],
    deep:    ['#4d4d55', '#45454d', '#3a3a41', '#55555d', '#404047'],
    bedrock: ['#575757', '#333333', '#1e1e1e', '#6e6e6e', '#262626'],
  };

  function canvas(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }

  // 16×16 тайл блока
  function tile(kind, seed = 7) {
    const c = canvas(16, 16), g = c.getContext('2d'), r = rng(seed);
    for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
      let col = pick(r, PAL[kind]);
      if (kind === 'deep' && y % 4 === 0 && r() < .6) col = '#36363c';           // слоистость сланца
      if (kind === 'bedrock' && r() < .25) col = r() < .5 ? '#111' : '#8a8a8a';  // пятна бедрока
      g.fillStyle = col; g.fillRect(x, y, 1, 1);
    }
    return c;
  }

  // руда = камень + кластеры цвета
  const ORE = {
    diamond:  ['#a5f3ef', '#5EE6D8', '#2bb3a8'],
    emerald:  ['#9ff5b8', '#2fd36b', '#15914a'],
    gold:     ['#fff3a0', '#F2C14E', '#b8862a'],
    redstone: ['#ff9a8a', '#e0302a', '#8f1a14'],
    lapis:    ['#9fb8ff', '#3a63d8', '#1d3a8f'],
  };
  function oreTile(kind, seed) {
    const c = tile('stone', seed), g = c.getContext('2d'), r = rng(seed * 31);
    for (let k = 0; k < 5; k++) {
      const cx = 2 + Math.floor(r() * 11), cy = 2 + Math.floor(r() * 11);
      [[0, 0], [1, 0], [0, 1], [1, 1], [-1, 0]].forEach(([dx, dy], i) => {
        if (i > 2 && r() < .5) return;
        g.fillStyle = ORE[kind][(i + k) % 3]; g.fillRect(cx + dx, cy + dy, 1, 1);
      });
    }
    return c;
  }

  // трещины (как при добыче блока)
  function crackTile() {
    const c = canvas(16, 16), g = c.getContext('2d'), r = rng(99);
    g.fillStyle = 'rgba(0,0,0,.85)';
    const walk = (x, y, n) => {
      for (let i = 0; i < n; i++) {
        g.fillRect(x, y, 1, 1);
        x = Math.max(0, Math.min(15, x + Math.round(r() * 2 - 1)));
        y = Math.max(0, Math.min(15, y + (r() < .5 ? 1 : 0) - (r() < .35 ? 1 : 0)));
      }
    };
    walk(7, 7, 14); walk(8, 8, 12); walk(3, 2, 8); walk(12, 11, 8); walk(5, 12, 6);
    return c;
  }

  // зубчатый шов: пиксели верхнего слоя «свисают» в нижний
  function seam(upper, seed) {
    const W = 64, H = 12, c = canvas(W, H), g = c.getContext('2d'), r = rng(seed);
    let h = 4;
    for (let x = 0; x < W; x++) {
      if (x % 2 === 0) h = Math.max(1, Math.min(H - 1, h + Math.round(r() * 4 - 2)));
      for (let y = 0; y < h; y++) { g.fillStyle = pick(r, PAL[upper]); g.fillRect(x, y, 1, 1); }
    }
    return c;
  }

  // полоса земли под небом: травинки → дёрн → земля, темнеет книзу (стык с секцией «Земля»)
  function ground() {
    const W = 64, H = 32, c = canvas(W, H), g = c.getContext('2d'), r = rng(4);
    for (let x = 0; x < W; x++) {
      const tuft = r() < .3 ? 1 + Math.floor(r() * 2) : 0;
      for (let y = 8 - tuft; y < 8; y++) { g.fillStyle = pick(r, PAL.grass); g.fillRect(x, y, 1, 1); }
      if (r() < .05) { g.fillStyle = r() < .5 ? '#E0564A' : '#F2C14E'; g.fillRect(x, 6, 1, 1); }
      const drip = 1 + Math.floor(r() * 3);
      for (let y = 8; y < H; y++) {
        g.fillStyle = y < 11 + drip ? pick(r, PAL.grass) : pick(r, PAL.dirt);
        g.fillRect(x, y, 1, 1);
      }
    }
    const fade = g.createLinearGradient(0, 14, 0, H);
    fade.addColorStop(0, 'rgba(26,16,8,0)'); fade.addColorStop(1, 'rgba(30,20,12,.62)');
    g.fillStyle = fade; g.fillRect(0, 14, W, H - 14);
    return c;
  }

  // 8×8 иконки-спрайты: палитра + карта строк
  const ICONS = {
    grass:    [{ G: '#6BBF3B', g: '#4E9427', D: '#866043', d: '#5d4128' }, 'GGGGGGGG GgGGgGGg gDgDDgDd DDdDDDdD DdDDdDDD DDDdDDdD dDDDDdDD DDdDDDDd'],
    chest:    [{ O: '#4a3216', B: '#a1712e', Y: '#e0c060' }, 'OOOOOOOO OBBBBBBO OBBBBBBO OOOYYOOO OBBYYBBO OBBBBBBO OBBBBBBO OOOOOOOO'],
    anvil:    [{ A: '#55585c', H: '#7c8085' }, '........ HHHHHHH. .AAAAAAA ...AAA.. ...AAA.. ..HAAAA. .AAAAAAA ........'],
    map:      [{ P: '#e8dcb0', G: '#6BBF3B', B: '#5EB8E6' }, 'PPPPPPPP PGGPPBBP PGGGPBBP PPGPPPBP PBPPGGPP PBBPGGGP PPBPPGPP PPPPPPPP'],
    book:     [{ R: '#7a3b2a', W: '#e8dcb0', Y: '#F2C14E' }, '.RRRRRR. RRWWWWRR RRWWWWRR RRRRRRRR RRYRRRRR RRRRRRRR RRRRRRRR .RRRRRR.'],
    compass:  [{ O: '#5a5a5a', G: '#9a9a9a', R: '#E0564A', W: '#eeeeee' }, '..OOOO.. .OGGGGO. OGGRGGGO OGGRGGGO OGGWGGGO OGGWGGGO .OGGGGO. ..OOOO..'],
    clock:    [{ O: '#b8862a', G: '#F2C14E', K: '#3a2a10' }, '..OOOO.. .OGGGGO. OGGKGGGO OGGKGGGO OGGKKKGO OGGGGGGO .OGGGGO. ..OOOO..'],
    painting: [{ O: '#6e4f2c', S: '#7cc0ee', Y: '#fff6b8', G: '#6BBF3B', g: '#4E9427', D: '#8a6a3f' }, 'OOOOOOOO OSSSSSSO OSSSYSSO OSSSSSSO OGGGGGGO OGgGGgGO ODDDDDDO OOOOOOOO'],
    key:      [{ Y: '#F2C14E', y: '#b8862a' }, '..YYY... .Y...Y.. .y...y.. ..YYY... ...Y.... ...YY... ...Y.... ...yy...'],
    steve:    [{ H: '#3b2a1a', S: '#c69c7a', W: '#ffffff', B: '#4b3ca8', n: '#8a5a3c' }, 'HHHHHHHH HHHHHHHH SSSSSSSS SWBSSBWS SSSnnSSS SSnSSnSS SSnnnnSS SSSSSSSS'],
    sign:     [{ W: '#b8935a', w: '#6e4f2c', T: '#6e4f2c' }, 'WWWWWWWW WwwwwwwW WWWWWWWW WwwwwwWW WWWWWWWW ...TT... ...TT... ...TT...'],
    cherry:   [{ P: '#f4b8d0', p: '#e88bb0', T: '#5a3a2a' }, '.PPpP... PPpPPPp. pPPPpPPP .PpPPPp. ...TT... ...TT... ..TTTT.. ........'],
    pick:     [{ L: '#a5f3ef', D: '#2bb3a8', S: '#7a5a34' }, '.LLLLL.. L....DL. .....SD. ....S.L. ...S..L. ..S..... .S...... S.......'],
  };
  const GEM = '..LLLL.. .LWLLLL. LWLLLLDL LLLLLLDL .LLLLDL. ..LLDL.. ...LL... ........';
  Object.entries(ORE).forEach(([k, [w, l, d]]) => { ICONS[k] = [{ W: w, L: l, D: d }, GEM]; });
  ICONS.amethyst = [{ W: '#e3c2ff', L: '#a86fe0', D: '#6b3fa0' }, GEM];

  function drawIcon(cv, name) {
    const g = cv.getContext('2d');
    g.clearRect(0, 0, 8, 8);
    if (name === 'bedrock') { g.drawImage(tile('bedrock', 3), 0, 0, 8, 8); return; }
    const def = ICONS[name]; if (!def) return;
    const [pal, map] = def;
    map.split(' ').forEach((row, y) => [...row].forEach((ch, x) => {
      if (ch === '.') return;
      g.fillStyle = pal[ch]; g.fillRect(x, y, 1, 1);
    }));
  }

  // ── применяем текстуры ──
  const url = c => `url(${c.toDataURL()})`;
  const grassSide = () => {
    const c = tile('dirt', 5), g = c.getContext('2d'), r = rng(2);
    for (let x = 0; x < 16; x++) { const h = 3 + Math.floor(r() * 3); for (let y = 0; y < h; y++) { g.fillStyle = pick(r, PAL.grass); g.fillRect(x, y, 1, 1); } }
    return c;
  };
  const TEX = { grass: grassSide(), dirt: tile('dirt', 11), stone: tile('stone', 21), deep: tile('deep', 31), bedrock: tile('bedrock', 41) };
  const UPPER = { stone: 'dirt', deep: 'stone', bedrock: 'deep' };

  document.documentElement.style.setProperty('--crack', url(crackTile()));
  $$('.layer[data-tex]').forEach(s => s.style.setProperty('--tex', url(TEX[s.dataset.tex])));
  $$('.seam[data-seam]').forEach((el, i) => { const up = UPPER[el.dataset.seam]; if (up) el.style.backgroundImage = url(seam(up, 50 + i)); });
  $$('.depth i[data-tex]').forEach(el => { el.style.backgroundImage = url(TEX[el.dataset.tex]); });
  const gr = $('.ground'); if (gr) gr.style.backgroundImage = url(ground());
  $$('canvas[data-icon]').forEach(cv => drawIcon(cv, cv.dataset.icon));
  $$('canvas').forEach(cv => { cv.style.imageRendering = 'pixelated'; cv.setAttribute('aria-hidden', 'true'); });

  // занятая кнопка остаётся в фокусе: aria-disabled вместо disabled
  const busy = (btn, on) => on ? btn.setAttribute('aria-disabled', 'true') : btn.removeAttribute('aria-disabled');
  const isBusy = btn => btn.getAttribute('aria-disabled') === 'true';

  // ── шапка + HUD: координата Y интерполируется между якорями слоёв ──
  const top = $('#top');
  const layers = $$('main > section[data-y]');
  const depthLinks = $$('.depth a');
  const hudY = $('#hudY'), hudLayer = $('#hudLayer'), hudBiome = $('#hudBiome');
  const clouds = $$('.cloud');
  let current = null, ticking = false;

  function onScroll() {
    ticking = false;
    top.classList.toggle('solid', scrollY > 30);
    // облака сдвигаются только от прокрутки — без бесконечной автоанимации
    clouds.forEach(c => { c.style.transform = `translateX(calc(${c.dataset.x}vw + ${scrollY * c.dataset.v}px))`; });
    const ref = scrollY + innerHeight * .4;
    const tops = layers.map(s => s.offsetTop);
    let i = 0;
    while (i < layers.length - 1 && ref >= tops[i + 1]) i++;
    const a = +layers[i].dataset.y, next = layers[i + 1];
    let y = a;
    if (next) y = a + (+next.dataset.y - a) * Math.min(1, (ref - tops[i]) / (tops[i + 1] - tops[i]));
    // упёрлись в низ страницы — ровно бедрок
    if (scrollY + innerHeight >= document.documentElement.scrollHeight - 4) { y = -64; i = layers.length - 1; }
    hudY.textContent = y.toFixed(3);
    const s = layers[i];
    if (s !== current) {
      current = s;
      hudLayer.textContent = s.dataset.name;
      hudBiome.textContent = s.dataset.biome;
      depthLinks.forEach(l => { const on = l.dataset.layer === s.id; l.classList.toggle('on', on); on ? l.setAttribute('aria-current', 'location') : l.removeAttribute('aria-current'); });
      enterLayer(s.id);
    }
  }
  addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(onScroll); } }, { passive: true });
  addEventListener('resize', onScroll);

  // ── достижения (тост как в игре) ──
  const adv = $('#adv'), advText = $('#advText'), advIcon = $('#advIcon');
  const queue = [], got = new Set();
  let showing = false;
  function achieve(id, text, icon) {
    if (got.has(id)) return;
    got.add(id); queue.push([text, icon]);
    if (!showing) nextAdv();
  }
  async function nextAdv() {
    const item = queue.shift();
    if (!item) { showing = false; return; }
    showing = true;
    advText.textContent = item[0]; drawIcon(advIcon, item[1]);
    adv.classList.add('on'); await wait(3600);
    adv.classList.remove('on'); await wait(600);
    nextAdv();
  }
  function enterLayer(id) {
    if (id === 'rescue') achieve('stone', 'Каменный век', 'pick');
    if (id === 'download') achieve('bedrock', 'Глубже некуда', 'bedrock');
  }

  // ── появление при прокрутке ──
  if ('IntersectionObserver' in window && !reduced) {
    const io = new IntersectionObserver(es => es.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
    }), { rootMargin: '0px 0px -8% 0px' });
    $$('.rise').forEach(el => io.observe(el));
  } else $$('.rise').forEach(el => el.classList.add('in'));

  // ── макет: «Играть» ──
  const play = $('#mockPlay'), xp = $('#mockXp'), bar = xp.firstElementChild;
  const mTitle = $('#mockTitle'), mSub = $('#mockSub'), toast = $('#mockToast');
  play.addEventListener('click', async () => {
    if (isBusy(play)) return;
    busy(play, true); xp.classList.add('on');
    mTitle.textContent = 'Запуск…';
    for (const [t, p] of [['Проверка файлов…', 30], ['Java 21 · флаги Aikar', 62], ['Запуск Fabric 1.21.1', 100]]) {
      mSub.textContent = t; bar.style.width = p + '%'; await wait(650);
    }
    toast.classList.add('on'); mTitle.textContent = 'В игре'; mSub.textContent = 'Приятной игры!';
    await wait(2600);
    toast.classList.remove('on'); xp.classList.remove('on'); bar.style.width = '0';
    mTitle.textContent = 'Готов к запуску'; mSub.textContent = 'Выбери версию и нажми «Играть».';
    busy(play, false);
  });

  // ── демо установки .mrpack ──
  const chips = $$('.chip[data-loader]'), stLoader = $('#stLoader'), stMods = $('#stMods');
  const steps = $$('#steps li'), dlRun = $('#dlRun'), dlNote = $('#dlNote');
  let loader = 'Fabric';
  chips.forEach(ch => ch.addEventListener('click', () => {
    if (isBusy(dlRun)) return;
    chips.forEach(c => c.setAttribute('aria-pressed', String(c === ch)));
    loader = ch.dataset.loader;
    stLoader.textContent = /Forge/.test(loader) ? `Загрузчик ${loader} · headless` : `Загрузчик ${loader}`;
  }));
  dlRun.addEventListener('click', async () => {
    if (isBusy(dlRun)) return;
    busy(dlRun, true);
    steps.forEach(li => { li.className = ''; li.querySelector('em').textContent = '—'; });
    stMods.textContent = '0/86'; dlNote.textContent = 'Загрузка…';
    for (const [i, li] of steps.entries()) {
      li.className = 'run';
      if (i === 2) { for (let n = 0; n < 86; n += 6) { stMods.textContent = `${n}/86`; await wait(55); } stMods.textContent = '86/86'; }
      else await wait(420);
      li.className = 'done';
      if (i !== 2) li.querySelector('em').textContent = '✔';
    }
    dlNote.textContent = `✔ blockify-packs/vyzhivanie-plus/ · ${loader}`;
    dlRun.textContent = 'Ещё раз'; busy(dlRun, false);
  });

  // ── Crash Doctor ──
  const log = $('#log'), diag = $('#diag'), crashRun = $('#crashRun'), crashFix = $('#crashFix'), crashFixed = $('#crashFixed');
  const baseLog = log.innerHTML;
  const crashLines = [
    ['dim', '[12:04:34] [Render thread/INFO]: Backend library: LWJGL 3.3.3'],
    ['err', '[12:04:35] [Render thread/ERROR]: Mixin apply failed: core.MixinWorldRenderer'],
    ['err', 'MixinTransformerError: target already transformed by another mod'],
    ['dim', '    at net.minecraft.client.render.WorldRenderer.render'],
    ['err', '---- Minecraft Crash Report ----'],
    ['dim', '// Who set us up the TNT?'],
  ];
  crashRun.addEventListener('click', async () => {
    if (isBusy(crashRun)) return;
    busy(crashRun, true); diag.hidden = true; crashFixed.hidden = true; busy(crashFix, false);
    log.innerHTML = baseLog;
    for (const [cls, text] of crashLines) {
      const s = document.createElement('span');
      s.className = cls; s.textContent = '\n' + text;
      log.appendChild(s); await wait(380);
    }
    await wait(300);
    diag.hidden = false; busy(crashRun, false); crashRun.textContent = 'Ещё раз';
    crashFix.focus({ preventScroll: true });
  });
  crashFix.addEventListener('click', () => {
    if (isBusy(crashFix)) return;
    busy(crashFix, true); crashFixed.hidden = false;
    addSnapshot({ label: 'авто · фикс', mods: 85, ok: true });
  });

  // ── машина времени ──
  const track = $('#track'), tmState = $('#tmState');
  const snaps = [
    { label: 'пн · чистая', mods: 72, ok: true },
    { label: 'ср · шейдеры', mods: 79, ok: true },
    { label: 'пт · апдейт', mods: 86, ok: false },
  ];
  let active = snaps.length - 1;
  function renderTrack(keepFocus) {
    track.innerHTML = '';
    snaps.forEach((s, i) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'snap' + (s.ok ? '' : ' bad');
      b.style.left = (snaps.length === 1 ? 50 : i / (snaps.length - 1) * 100) + '%';
      b.setAttribute('aria-pressed', String(i === active));
      b.innerHTML = '<i aria-hidden="true"></i>';
      b.append(s.label);
      b.addEventListener('click', () => { active = i; renderTrack(true); });
      track.appendChild(b);
    });
    const s = snaps[active], latest = active === snaps.length - 1;
    const l = document.createElement('span');
    l.textContent = `${latest ? 'Сейчас' : 'Откат к'} «${s.label}» · ${s.mods} модов`;
    const r = document.createElement('span');
    r.className = s.ok ? 'ok' : 'bad-t'; r.textContent = s.ok ? '✔ запускается' : '✖ краш при запуске';
    tmState.replaceChildren(l, r);
    if (keepFocus) track.children[active].focus();
  }
  function addSnapshot(s) { snaps.push(s); if (snaps.length > 5) snaps.shift(); active = snaps.length - 1; renderTrack(); }
  renderTrack();

  // ── пасхалка: руда в слое камня (4 удара на блок) ──
  const field = $('#ores'), oreCount = $('#oreCount'), hudOres = $('#hudOres');
  const spots = [['7%', '52px', 'diamond'], ['31%', '64px', 'gold'], ['74%', '48px', 'emerald'], ['18%', 'calc(100% - 92px)', 'redstone'], ['86%', 'calc(100% - 84px)', 'lapis']];
  let mined = 0;
  spots.forEach(([x, y, kind], i) => {
    const b = document.createElement('div');
    b.className = 'ore';
    b.style.left = x; b.style.top = y;
    b.style.backgroundImage = url(oreTile(kind, 70 + i));
    let hits = 0;
    b.addEventListener('click', () => {
      hudOres.hidden = false;
      hits++; b.style.setProperty('--dmg', Math.min(.9, hits / 4));
      b.classList.remove('hit'); void b.offsetWidth; b.classList.add('hit');
      if (hits === 4) {
        b.classList.add('gone'); mined++; oreCount.textContent = mined;
        if (mined === 1) achieve('ore1', 'Шахтёр', kind);
        if (mined === spots.length) achieve('oreAll', 'Алмазы! Вся руда добыта', 'diamond');
      }
    });
    field.appendChild(b);
  });

  // ── «Скачать»: если в последнем релизе уже есть portable-архив — ведём прямо на файл ──
  // Без ответа API (лимит, офлайн, релиза ещё нет) кнопки остаются ссылкой на страницу релизов.
  const REPO = 'Blockify-Launcher/Blockify-Launcher';
  const ASSET = 'Blockify-portable-win-x64.zip';
  const dlLinks = $$('a[data-dl]'), dlMeta = $('#dlMeta'), dlHash = $('#dlHash'), dlHashValue = $('#dlHashValue');
  if (dlLinks.length && 'fetch' in window) {
    fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: 'application/vnd.github+json' } })
      .then(r => r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)))
      .then(rel => {
        const a = (rel.assets || []).find(x => x && x.name === ASSET);
        const okUrl = a && typeof a.browser_download_url === 'string'
          && a.browser_download_url.startsWith(`https://github.com/${REPO}/releases/download/`);
        if (!okUrl || rel.draft || rel.prerelease) return;
        dlLinks.forEach(l => { l.href = a.browser_download_url; });
        const ver = String(rel.tag_name || '').replace(/^v/, '');
        const size = a.size > 0 ? ` · ${Math.round(a.size / 1048576)} МБ` : '';
        dlMeta.textContent = `Версия ${ver}${size} · ${ASSET} · установка не нужна`;
        const m = /^sha256:([0-9a-f]{64})$/i.exec(a.digest || '');
        if (m) { dlHashValue.textContent = m[1].toLowerCase(); dlHash.hidden = false; }
      })
      .catch(() => {});
  }

  // ── предупреждение, если система явно не подходит (Windows 10 1607+/11, только 64-бит) ──
  const osNote = $('#osNote');
  const warnOs = text => { osNote.textContent = text; osNote.hidden = false; };
  const ua = navigator.userAgent || '';
  const uad = navigator.userAgentData;
  const isWin = uad && uad.platform ? uad.platform === 'Windows' : /Windows NT/.test(ua);
  if (!isWin) {
    if (/Android|iPhone|iPad|Macintosh|Mac OS X|Linux|CrOS/.test(ua) || (uad && uad.platform))
      warnOs('Blockify работает только на Windows 10 и 11 (64-бит). Откройте эту страницу на компьютере с Windows.');
  } else if (/Windows NT (5\.|6\.[0-3])/.test(ua)) {
    warnOs('Похоже, у вас Windows 7 или 8.1 — на ней Blockify не запустится. Нужна Windows 10 (1607+) или Windows 11, 64-бит.');
  } else if (uad && typeof uad.getHighEntropyValues === 'function') {
    uad.getHighEntropyValues(['bitness', 'architecture']).then(h => {
      if (h.bitness === '32') warnOs('Похоже, у вас 32-битная Windows — Blockify работает только на 64-битной.');
      else if (h.architecture === 'arm') warnOs('Windows на ARM официально не поддерживается: на Windows 11 лаунчер может работать через эмуляцию x64, на Windows 10 ARM — нет.');
    }).catch(() => {});
  } else if (!/Win64|WOW64|x64|amd64/i.test(ua)) {
    warnOs('Похоже, у вас 32-битная Windows — Blockify работает только на 64-битной.');
  }

  onScroll();
})();
