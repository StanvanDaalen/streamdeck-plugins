// Netflix tells Windows nothing but "Netflix". This reads the episode, series and picture from Netflix's own
// player data and hands them to the browser's media session, which passes them on to Windows.
(() => {
  const metadataCache = new Map(); // movie id -> metadata (or null), so each title is fetched once
  let shown = '';

  const firstUrl = list => Array.isArray(list) && list.length ? (list[0].url || null) : null;

  // The id in /watch/<id>.
  const watchId = () => (location.pathname.match(/^\/watch\/(\d+)/) || [])[1] || null;

  // 1. Netflix's player state, already loaded in the page.
  function fromPlayerState(id) {
    try {
      const all = netflix.appContext.state.playerApp.getState().videoPlayer.videoMetadata;
      const entry = all[id] || Object.values(all).find(m => m && m._metadata && m._metadata.video);
      return entry && entry._metadata && entry._metadata.video || null;
    } catch { return null; }
  }

  // 2. Otherwise Netflix's metadata service, the one its own player uses (signed in through the page's cookies).
  async function fromService(id) {
    if (metadataCache.has(id)) return metadataCache.get(id);
    metadataCache.set(id, null);
    let build = 'release';
    try { build = netflix.reactContext.models.serverDefs.data.BUILD_IDENTIFIER || build; } catch {}
    try {
      const r = await fetch(`/nq/website/memberapi/${build}/metadata?movieid=${id}`, { credentials: 'include' });
      if (r.ok) {
        const video = (await r.json()).video || null;
        metadataCache.set(id, video);
        return video;
      }
    } catch {}
    return null;
  }

  // A video object as { title, series, code, picture }.
  function describe(video, id) {
    if (!video) return null;
    if (video.type === 'show' && Array.isArray(video.seasons)) {
      const current = String(video.currentEpisode || id);
      for (const season of video.seasons)
        for (const ep of season.episodes || [])
          if (String(ep.id) === current || String(ep.episodeId) === current || String(ep.id) === String(id))
            return {
              title: ep.title || `Episode ${ep.seq}`,
              series: video.title || '',
              code: `S${season.seq}:E${ep.seq}`,
              picture: firstUrl(ep.stills) || firstUrl(ep.thumbs) || firstUrl(video.artwork) || firstUrl(video.boxart),
            };
    }
    return { title: video.title || '', series: '', code: '', picture: firstUrl(video.artwork) || firstUrl(video.storyart) || firstUrl(video.boxart) };
  }

  // 3. Last resort: the title the player shows while its controls are visible.
  function fromScreen() {
    const el = document.querySelector('[data-uia="video-title"]');
    if (!el) return null;
    const h4 = el.querySelector('h4');
    const spans = [...el.querySelectorAll('span')].map(s => s.textContent.trim()).filter(Boolean);
    if (h4) return { title: spans[1] || spans[0] || '', series: h4.textContent.trim(), code: spans[1] ? spans[0] : '', picture: null };
    return { title: el.textContent.trim(), series: '', code: '', picture: null };
  }

  async function update() {
    const id = watchId();
    if (!id || !('mediaSession' in navigator)) { shown = ''; return; }
    const info = describe(fromPlayerState(id), id) || describe(await fromService(id), id) || fromScreen();
    if (!info || !info.title) return;
    const key = JSON.stringify(info);
    if (key === shown && navigator.mediaSession.metadata) return;
    shown = key;
    navigator.mediaSession.metadata = new MediaMetadata({
      title: info.title,
      artist: info.series,
      album: info.code,
      artwork: info.picture ? [{ src: info.picture, sizes: '1280x720' }] : [],
    });
  }

  // Netflix changes pages without reloading (and plays the next episode by itself), so check now and then.
  setInterval(update, 2000);
  update();
})();
