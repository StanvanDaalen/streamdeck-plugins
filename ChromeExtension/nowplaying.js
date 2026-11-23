// Stream Deck Now Playing: many streaming sites tell Windows nothing useful ("Netflix", or only the page title).
// This reads what is playing from the page and hands it to the browser's media session, which passes it on to
// Windows: the episode (title) and series (artist) with a still, or for motorsport the event and session (the
// plugin adds the country's flag). A site that already reports a proper title is left alone. The Stream Deck plugin tidies the
// text further (splitting "Series S1:E3 Episode", "Spanish Grand Prix - Race", …).
(() => {
  const host = location.hostname;
  const MOTORSPORT = /(^|\.)(f1tv\.formula1\.com|motogp\.com|fiawec\.(tv|com))$/.test(host);

  let ours = null;  // the metadata this script set last
  let shown = '';

  const clean = s => (s || '').replace(/\s+/g, ' ').trim();
  const usable = s => { s = clean(s); return s.length > 1 && s.length < 140 ? s : ''; };

  // ---------- Netflix: its own player data (it reports nothing itself) ----------
  const netflixCache = new Map();
  const firstUrl = list => Array.isArray(list) && list.length ? (list[0].url || null) : null;

  function netflixDescribe(video, id) {
    if (!video) return null;
    if (video.type === 'show' && Array.isArray(video.seasons)) {
      const current = String(video.currentEpisode || id);
      for (const season of video.seasons)
        for (const ep of season.episodes || [])
          if (String(ep.id) === current || String(ep.episodeId) === current || String(ep.id) === String(id))
            return {
              title: ep.title || `Episode ${ep.seq}`, artist: video.title || '', album: `S${season.seq}:E${ep.seq}`,
              picture: firstUrl(ep.stills) || firstUrl(ep.thumbs) || firstUrl(video.artwork) || firstUrl(video.boxart),
            };
    }
    return { title: video.title || '', artist: '', album: '', picture: firstUrl(video.artwork) || firstUrl(video.storyart) || firstUrl(video.boxart) };
  }

  async function netflix() {
    const id = (location.pathname.match(/^\/watch\/(\d+)/) || [])[1];
    if (!id) return null;
    try {
      const all = netflix_state();
      const entry = all && (all[id] || Object.values(all).find(m => m && m._metadata && m._metadata.video));
      const info = netflixDescribe(entry && entry._metadata && entry._metadata.video, id);
      if (info && info.title) return info;
    } catch {}
    if (!netflixCache.has(id)) {
      netflixCache.set(id, null);
      // "release" is the current path; the build id was the path in older Netflix versions (it now answers 502).
      const paths = ['release'];
      try { const b = window.netflix.reactContext.models.serverDefs.data.BUILD_IDENTIFIER; if (b) paths.push(b); } catch {}
      for (const path of paths) {
        try {
          const r = await fetch(`/nq/website/memberapi/${path}/metadata?movieid=${id}`, { credentials: 'include' });
          if (r.ok) { netflixCache.set(id, (await r.json()).video || null); break; }
        } catch {}
      }
    }
    const info = netflixDescribe(netflixCache.get(id), id);
    if (info && info.title) return info;
    const el = document.querySelector('[data-uia="video-title"]');
    if (!el) return null;
    const h4 = el.querySelector('h4');
    const spans = [...el.querySelectorAll('span')].map(s => clean(s.textContent)).filter(Boolean);
    return h4 ? { title: spans.join(' '), artist: clean(h4.textContent) } : { title: clean(el.textContent), artist: '' };
  }
  function netflix_state() {
    try { return window.netflix.appContext.state.playerApp.getState().videoPlayer.videoMetadata; } catch { return null; }
  }

  // ---------- Known player overlays: [title (series/show), subtitle (episode)] ----------
  const OVERLAYS = [
    ['.atvwebplayersdk-title-text', '.atvwebplayersdk-subtitle-text'],                       // Prime Video
    ['[data-testid="player-ux-asset-title"]', '[data-testid="player-ux-asset-subtitle"]'],   // HBO Max
    ['.title-field', '.subtitle-field'],                                                     // Disney+
    ['[data-testid*="title" i]:not([data-testid*="subtitle" i])', '[data-testid*="subtitle" i]'],
  ];

  function fromOverlay() {
    for (const [t, s] of OVERLAYS) {
      const title = usable(document.querySelector(t)?.textContent);
      if (!title) continue;
      const sub = usable(document.querySelector(s)?.textContent);
      return sub ? { title: sub, artist: title } : { title, artist: '' };
    }
    return null;
  }

  // Elements named like a title near the playing video (works for players not listed above).
  function nearVideo(video) {
    let root = video;
    for (let i = 0; i < 8 && root.parentElement && root.parentElement !== document.body; i++) root = root.parentElement;
    const names = el => `${typeof el.className === 'string' ? el.className : ''} ${el.getAttribute('data-testid') || ''} ${el.getAttribute('data-uia') || ''}`;
    const all = [...root.querySelectorAll('*')].filter(el => el.children.length <= 3 && !el.closest('button') && usable(el.textContent));
    const sub = all.find(el => /sub-?title|episode|secondary/i.test(names(el)));
    const title = all.find(el => /title/i.test(names(el)) && !/sub-?title/i.test(names(el)));
    if (!title) return null;
    const t = usable(title.textContent), s = sub && sub !== title ? usable(sub.textContent) : '';
    return s && s !== t ? { title: s, artist: t } : { title: t, artist: '' };
  }

  // Schema.org data some sites put in the page (TV episode, movie, sports event).
  function fromJsonLd() {
    for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
      try {
        for (const d of [].concat(JSON.parse(script.textContent))) {
          const type = String(d['@type'] || '');
          if (/Episode/.test(type)) {
            const series = d.partOfSeries?.name || d.partOfTVSeries?.name || '';
            const season = d.partOfSeason?.seasonNumber, ep = d.episodeNumber;
            return { title: d.name || '', artist: series, album: season && ep ? `S${season}:E${ep}` : '', picture: [].concat(d.image || [])[0]?.url || [].concat(d.image || [])[0] || null };
          }
          if (/Movie|SportsEvent|BroadcastEvent|VideoObject/.test(type) && d.name)
            return { title: d.name, artist: '', picture: [].concat(d.image || d.thumbnailUrl || [])[0]?.url || [].concat(d.image || d.thumbnailUrl || [])[0] || null };
        }
      } catch {}
    }
    return null;
  }

  const meta = name => document.querySelector(`meta[property="${name}"],meta[name="${name}"]`)?.content || '';

  // The site's own report is fine when it names more than the site itself.
  function siteReportsEnough() {
    const m = navigator.mediaSession?.metadata;
    if (!m || m === ours) return false;
    const title = clean(m.title);
    const siteName = clean(meta('og:site_name')) || host.replace(/^www\./, '').split('.')[0];
    return title.length > 1 && !new RegExp(`^${siteName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i').test(title)
      && title.toLowerCase() !== host && (clean(m.artist).length > 0 || m.artwork.length > 0);
  }

  async function describe(video) {
    // Netflix: only while watching (its home page plays trailers too).
    if (host.endsWith('netflix.com')) return location.pathname.startsWith('/watch') ? netflix() : null;
    const found = fromOverlay() || nearVideo(video) || fromJsonLd();
    const title = found?.title || usable(meta('og:title')) || usable(document.title);
    if (!title) return null;
    return {
      title,
      artist: found?.artist || '',
      album: found?.album || '',
      picture: found?.picture || meta('og:image') || null,
    };
  }

  async function update() {
    if (!('mediaSession' in navigator)) return;
    const video = [...document.querySelectorAll('video')].find(v => v.currentTime > 0 || !v.paused);
    if (!video) { shown = ''; return; }
    // Other sites keep their own report when it is good enough; motorsport is always combined (see below).
    if (!MOTORSPORT && siteReportsEnough()) return;
    const info = await describe(video);
    if (!info) return;
    if (MOTORSPORT) {
      // The plugin turns the text into the event (top), the session (below) and the country's flag; here the
      // page's titles are combined so both parts are in it, e.g. "Spanish Grand Prix" + "Race".
      const own = navigator.mediaSession.metadata && navigator.mediaSession.metadata !== ours ? clean(navigator.mediaSession.metadata.title) : '';
      info.title = [...new Set([info.artist, info.title, own, usable(document.title)].filter(Boolean))].join(' - ');
      info.artist = '';
      info.album = '';
    }
    const key = JSON.stringify(info);
    if (key === shown && navigator.mediaSession.metadata === ours) return;
    shown = key;
    ours = new MediaMetadata({
      title: info.title,
      artist: info.artist || '',
      album: info.album || '',
      artwork: info.picture ? [{ src: info.picture }] : [],
    });
    navigator.mediaSession.metadata = ours;
  }

  // These sites change pages without reloading (and play the next episode by themselves), so check now and then.
  setInterval(() => update().catch(() => {}), 2000);
})();
