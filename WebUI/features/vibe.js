// «Живой вайб» — ambient background that follows the time of day, the season
// and the colours of the user's modpacks. Pure front-end: recolours the .bgfx
// blobs and tints the Home hero; nothing is sent to the host.
(function () {
  'use strict';

  const STORE_KEY = 'blockify.vibe';
  const TICK_MS = 4 * 60 * 1000;
  const LEAVE_DELAY = 220;

  // ── colour helpers (rgb arrays 0..255) ──
  const hex = h => { h = h.replace('#', ''); return [0, 2, 4].map(i => parseInt(h.substr(i, 2), 16)); };
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const mix = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);
  const mixSet = (A, B, t) => A.map((c, i) => mix(c, B[i], t));
  const css = c => `rgb(${c.map(v => Math.round(clamp(v, 0, 255))).join(',')})`;
  const cssA = (c, a) => `rgba(${c.map(v => Math.round(clamp(v, 0, 255))).join(',')},${a})`;

  function toHsl([r, g, b]) {
    r /= 255; g /= 255; b /= 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2;
    if (mx === mn) return [0, 0, l];
    const d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
    let h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h * 60, s, l];
  }
  function fromHsl([h, s, l]) {
    h = ((h % 360) + 360) % 360 / 360;
    if (!s) return [l * 255, l * 255, l * 255];
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    const f = t => { t = (t + 1) % 1; return t < 1 / 6 ? p + (q - p) * 6 * t : t < 1 / 2 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p; };
    return [f(h + 1 / 3) * 255, f(h) * 255, f(h - 1 / 3) * 255];
  }
  const hueShift = (c, deg) => { const [h, s, l] = toHsl(c); return fromHsl([h + deg, s, l]); };
  // keep blobs dark and muted enough for the glass UI on top to stay readable
  function tame(c, maxL) {
    const [h, s, l] = toHsl(c);
    return fromHsl([h, Math.min(s, 0.68), clamp(l, 0.1, maxL || 0.33)]);
  }

  // ── time of day ──
  const TOD = {
    night: ['#1b2452', '#0f3546', '#2b1c4c'],
    dawn:  ['#5a2e3e', '#25405e', '#6a4420'],
    day:   ['#2f5c17', '#14424f', '#5a4514'],   // = the static defaults in index.html
    dusk:  ['#5e2a1a', '#3a1f50', '#6c4214'],
  };
  Object.keys(TOD).forEach(k => TOD[k] = TOD[k].map(hex));
  // anchor hours; palette is linearly blended between neighbours
  const ANCHORS = [[0, 'night'], [4.5, 'night'], [6.5, 'dawn'], [9.5, 'day'], [16.5, 'day'], [19, 'dusk'], [21.5, 'night'], [24, 'night']];
  function timePalette(d) {
    const h = d.getHours() + d.getMinutes() / 60;
    for (let i = 0; i < ANCHORS.length - 1; i++) {
      const [h0, a] = ANCHORS[i], [h1, b] = ANCHORS[i + 1];
      if (h >= h0 && h <= h1) return mixSet(TOD[a], TOD[b], h1 === h0 ? 0 : (h - h0) / (h1 - h0));
    }
    return TOD.night;
  }
  // 1 at midday, ~0.7 deep at night — dims hero tint and the grass strip
  function daylight(d) {
    const h = d.getHours() + d.getMinutes() / 60;
    return 0.7 + 0.3 * clamp(Math.sin((h - 5) / 15 * Math.PI), 0, 1);
  }

  // ── season ──
  const SEASON = {
    winter: { tint: ['#1d3a5e', '#2a4e6c', '#3a4868'], w: 0.34, sat: 0.8,  strip: ['#e3ecf3', '#c4d3df', '#f5f9fc'], sw: 0.5 },
    spring: { tint: ['#3d7a28', '#236a52', '#6a7c2a'], w: 0.24, sat: 1.0,  strip: ['#8fdc58', '#5aa832', '#a8e870'], sw: 0.15 },
    summer: { tint: ['#2f6c14', '#0e5c6e', '#7c5c10'], w: 0.16, sat: 1.18, strip: null, sw: 0 },
    autumn: { tint: ['#7a3a12', '#6c4a12', '#5c2814'], w: 0.32, sat: 1.0,  strip: ['#c98a2e', '#a4621e', '#dca844'], sw: 0.32 },
  };
  Object.values(SEASON).forEach(s => { s.tint = s.tint.map(hex); if (s.strip) s.strip = s.strip.map(hex); });
  // month → [season, intensity]; ramps in/out at the edges of each season
  const MONTHS = [['winter', 1], ['winter', 0.9], ['spring', 0.6], ['spring', 1], ['spring', 0.8], ['summer', 0.7],
                  ['summer', 1], ['summer', 0.9], ['autumn', 0.6], ['autumn', 1], ['autumn', 0.85], ['winter', 0.8]];

  function ambient(d) {
    const [name, k] = MONTHS[d.getMonth()], S = SEASON[name];
    let pal = mixSet(timePalette(d), S.tint, S.w * k);
    pal = pal.map(c => { const [h, s, l] = toHsl(c); return fromHsl([h, clamp(s * (1 + (S.sat - 1) * k), 0, 1), l]); });
    return { pal, season: S, k };
  }

  // ── pack palette ──
  const palettes = new Map();   // slug → { bg:[rgb×3], accent:rgb }
  const pending = new Map();    // slug → Promise
  const packInfo = new Map();   // slug → icon url

  function hashPalette(slug) {
    let h = 2166136261;
    for (let i = 0; i < slug.length; i++) { h ^= slug.charCodeAt(i); h = Math.imul(h, 16777619); }
    const hue = (h >>> 0) % 360, s = 0.45 + ((h >>> 9) % 20) / 100;
    return [fromHsl([hue, s, 0.42]), fromHsl([hue + 38, s * 0.9, 0.36]), fromHsl([hue - 52, s * 0.8, 0.4])];
  }

  function dominant(data) {
    const px = [];
    for (let i = 0; i < data.length; i += 4) {
      if (data[i + 3] < 160) continue;
      const r = data[i], g = data[i + 1], b = data[i + 2];
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 510;
      if (l < 0.08 || l > 0.92) continue;
      px.push([r, g, b]);
    }
    if (px.length < 12) return null;
    px.sort((a, b) => (a[0] * 0.3 + a[1] * 0.59 + a[2] * 0.11) - (b[0] * 0.3 + b[1] * 0.59 + b[2] * 0.11));
    const K = Math.min(5, px.length);
    let C = Array.from({ length: K }, (_, i) => px[Math.floor((i + 0.5) / K * px.length)].slice());
    const n = new Array(K).fill(0);
    for (let it = 0; it < 10; it++) {
      const acc = C.map(() => [0, 0, 0]); n.fill(0);
      for (const p of px) {
        let best = 0, bd = Infinity;
        for (let j = 0; j < K; j++) {
          const dr = p[0] - C[j][0], dg = p[1] - C[j][1], db = p[2] - C[j][2], dd = dr * dr + dg * dg + db * db;
          if (dd < bd) { bd = dd; best = j; }
        }
        acc[best][0] += p[0]; acc[best][1] += p[1]; acc[best][2] += p[2]; n[best]++;
      }
      C = C.map((c, j) => n[j] ? acc[j].map(v => v / n[j]) : c);
    }
    // favour colourful clusters over large grey ones
    const ranked = C.map((c, j) => ({ c, score: n[j] * (0.3 + toHsl(c)[1]) }))
      .filter(x => x.score > 0).sort((a, b) => b.score - a.score);
    const out = [];
    for (const { c } of ranked) {
      if (out.every(o => Math.hypot(o[0] - c[0], o[1] - c[1], o[2] - c[2]) > 30)) out.push(c);
      if (out.length === 3) break;
    }
    if (!out.length) return null;
    while (out.length < 3) out.push(hueShift(out[0], out.length === 1 ? 32 : -40));
    return out;
  }

  function loadPixels(url) {
    return new Promise(resolve => {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.decoding = 'async';
      const t = setTimeout(() => resolve(null), 8000);
      img.onload = () => {
        clearTimeout(t);
        try {
          const cv = document.createElement('canvas'); cv.width = cv.height = 40;
          const cx = cv.getContext('2d', { willReadFrequently: true });
          cx.drawImage(img, 0, 0, 40, 40);
          resolve(cx.getImageData(0, 0, 40, 40).data);   // throws if tainted
        } catch (e) { resolve(null); }
      };
      img.onerror = () => { clearTimeout(t); resolve(null); };
      img.src = url;
    });
  }

  function finalize(raw) {
    const accent = raw.slice().sort((a, b) => toHsl(b)[1] - toHsl(a)[1])[0];
    const [ah, as] = toHsl(accent);
    return {
      bg: raw.map(c => {
        const [h, s, l] = toHsl(c);
        return fromHsl([h, Math.min(s, 0.7) * 0.85, 0.15 + 0.15 * clamp(l, 0, 1)]);
      }),
      accent: fromHsl([ah, Math.max(as, 0.45), 0.5]),
    };
  }

  function ensurePalette(slug) {
    if (palettes.has(slug)) return Promise.resolve(palettes.get(slug));
    if (pending.has(slug)) return pending.get(slug);
    const icon = packInfo.get(slug);
    const p = (async () => {
      let data = icon ? await loadPixels(icon) : null;
      // the icon may already sit in the HTTP cache from a non-CORS CSS load; retry past it once
      if (!data && icon && /^https?:/i.test(icon)) data = await loadPixels(icon + (icon.includes('?') ? '&' : '?') + 'bv=1');
      let raw = null;
      try { raw = data && dominant(data); } catch (e) { }
      const pal = finalize(raw || hashPalette(slug));
      palettes.set(slug, pal);
      pending.delete(slug);
      return pal;
    })();
    pending.set(slug, p);
    return p;
  }

  // ── DOM ──
  const style = document.createElement('style');
  style.id = 'vibe-style';
  style.textContent = `
@property --vh1{syntax:'<color>';inherits:true;initial-value:rgba(0,0,0,0)}
@property --vh2{syntax:'<color>';inherits:true;initial-value:rgba(0,0,0,0)}
@property --vg1{syntax:'<color>';inherits:true;initial-value:#6BBF3B}
@property --vg2{syntax:'<color>';inherits:true;initial-value:#4E9427}
@property --vg3{syntax:'<color>';inherits:true;initial-value:#7ccf49}
.bgfx .blob{transition:background-color 1.2s ease}
.hero.vibe{transition:--vh1 1.2s ease,--vh2 1.2s ease,--vg1 1.2s ease,--vg2 1.2s ease,--vg3 1.2s ease;
  box-shadow:0 16px 40px rgba(0,0,0,.4),inset 0 1px 0 var(--glass-hi),
    inset 0 170px 150px -100px var(--vh1),inset -240px 0 200px -150px var(--vh2)}
.hero.vibe::before{background:repeating-linear-gradient(90deg,var(--vg1) 0 10px,var(--vg2) 10px 20px,var(--vg3) 20px 30px)}
@media (prefers-reduced-motion:reduce){.bgfx .blob,.hero.vibe{transition:none}}`;
  document.head.appendChild(style);

  const blobs = () => Array.from(document.querySelectorAll('.bgfx .blob'));
  const hero = () => document.querySelector('#s-home .hero') || document.querySelector('.hero');
  let stripBase = null;
  function grassBase() {
    if (stripBase) return stripBase;
    const cs = getComputedStyle(document.documentElement);
    const read = (v, d) => { const s = cs.getPropertyValue(v).trim(); return /^#[0-9a-f]{6}$/i.test(s) ? hex(s) : hex(d); };
    return (stripBase = [read('--grass', '#6BBF3B'), read('--grass-dark', '#4E9427'), hex('#7ccf49')]);
  }

  // ── state ──
  let enabled = true;
  try { enabled = localStorage.getItem(STORE_KEY) !== '0'; } catch (e) { }
  let hoverSlug = null, defaultSlug = null, leaveT = 0, tickT = 0, clock = null;

  function paint() {
    if (!enabled) return;
    const now = clock || new Date();
    const { pal: base, season, k } = ambient(now);
    const day = daylight(now);
    const slug = hoverSlug || defaultSlug;
    const pack = slug && palettes.get(slug);
    if (slug && !pack) ensurePalette(slug).then(paint);

    const w = !pack ? 0 : hoverSlug ? 0.78 : 0.32;
    const pal = (pack ? mixSet(base, pack.bg, w) : base).map(c => tame(c));
    blobs().forEach((b, i) => { b.style.backgroundColor = css(pal[i % 3]); });

    const h = hero();
    if (!h) return;
    h.classList.add('vibe');
    const glow = c => { const [hh, s] = toHsl(c); return fromHsl([hh, Math.min(1, s * 1.1), 0.42]); };
    h.style.setProperty('--vh1', cssA(glow(pal[0]), (0.34 * day).toFixed(3)));
    h.style.setProperty('--vh2', cssA(glow(pal[1]), (0.26 * day).toFixed(3)));
    let strip = grassBase();
    if (season.strip) strip = mixSet(strip, season.strip, season.sw * k);
    if (pack) strip = strip.map(c => mix(c, pack.accent, hoverSlug ? 0.38 : 0.14));
    strip = strip.map(c => c.map(v => v * (0.82 + 0.18 * day)));
    ['--vg1', '--vg2', '--vg3'].forEach((v, i) => h.style.setProperty(v, css(strip[i])));
  }

  function clear() {
    blobs().forEach(b => { b.style.backgroundColor = ''; });
    const h = hero();
    if (h) { h.classList.remove('vibe'); ['--vh1', '--vh2', '--vg1', '--vg2', '--vg3'].forEach(v => h.style.removeProperty(v)); }
  }

  function startTick() { clearInterval(tickT); tickT = setInterval(paint, TICK_MS); }
  function setEnabled(on) {
    enabled = !!on;
    try { localStorage.setItem(STORE_KEY, enabled ? '1' : '0'); } catch (e) { }
    if (enabled) { paint(); startTick(); } else { clearInterval(tickT); clear(); }
  }

  // ── packs ──
  function absorb(items) {
    (items || []).forEach(p => { if (p && p.slug) packInfo.set(p.slug, p.icon || ''); });
  }
  document.addEventListener('packs:rendered', e => {
    const items = (e.detail && e.detail.items) || [];
    absorb(items);
    let first = null;
    try { first = (typeof installedPacksCache !== 'undefined' && installedPacksCache[0]) || null; } catch (err) { }
    first = first || items[0] || null;
    defaultSlug = first ? first.slug : null;
    if (first) absorb([first]);
    // warm the cache so hovering recolours without a visible delay
    items.slice(0, 16).forEach(p => p && p.slug && ensurePalette(p.slug));
    paint();
  });

  const CARD = '.hp-card[data-slug], .mp-row[data-slug]';
  document.addEventListener('pointerover', e => {
    const el = e.target.closest && e.target.closest(CARD);
    if (!el) return;
    clearTimeout(leaveT);
    if (hoverSlug === el.dataset.slug) return;
    hoverSlug = el.dataset.slug;
    paint();
  });
  document.addEventListener('pointerout', e => {
    const el = e.target.closest && e.target.closest(CARD);
    if (!el || (e.relatedTarget && el.contains(e.relatedTarget))) return;
    clearTimeout(leaveT);
    // short grace period so sliding between adjacent cards doesn't flash the ambient palette
    leaveT = setTimeout(() => { hoverSlug = null; paint(); }, LEAVE_DELAY);
  });

  document.addEventListener('visibilitychange', () => { if (!document.hidden) paint(); });

  // ── settings toggle ──
  function mountToggle() {
    if (document.getElementById('swVibe')) return;
    const anchor = document.getElementById('swClose');
    const card = anchor && anchor.closest('.set-card');
    if (!card) return;
    const row = document.createElement('div');
    row.className = 'switch-row';
    row.title = 'Фон меняется со временем суток, сезоном и цветами твоих сборок';
    row.innerHTML = 'Живой фон <label class="sw"><input type="checkbox" id="swVibe"><i></i></label>';
    card.appendChild(row);
    const cb = row.querySelector('input');
    cb.checked = enabled;
    cb.onchange = () => setEnabled(cb.checked);
  }

  mountToggle();
  try { absorb(installedPacksCache); if (installedPacksCache[0]) defaultSlug = installedPacksCache[0].slug; } catch (e) { }
  if (enabled) { paint(); startTick(); }

  // debug hook (developers only — localStorage 'blockify.debug' = '1'):
  // BlockifyVibe.at(new Date(2026, 0, 1, 22)) previews a moment; .at(null) returns to now
  let debug = false;
  try { debug = localStorage.getItem('blockify.debug') === '1'; } catch (e) { }
  if (debug) {
    window.BlockifyVibe = {
      at(d) { clock = d || null; paint(); },
      palette: slug => ensurePalette(slug),
      set: setEnabled,
    };
  }
})();
