(() => {
  if (window.__islandMusicPage) return;
  window.__islandMusicPage = true;
  const handlers = new Map();
  let lastAudio = null, previous = '', lastSent = 0;
  const media = navigator.mediaSession;
  const clean = (value, limit) => typeof value === 'string' ? value.slice(0, limit) : '';
  const audios = () => [...document.querySelectorAll('audio,video')];
  function activeAudio() {
    return audios().find(a => !a.paused && !a.ended) || lastAudio || audios()[0];
  }
  function playButton() {
    return [...document.querySelectorAll('button[aria-label]')].find(b => !b.disabled && b.getClientRects().length > 0 && /^(Воспроизвести|Слушать|Play|Моя волна)$/i.test(b.getAttribute('aria-label') || ''));
  }
  function snapshot() {
    const audio = activeAudio(), metadata = media?.metadata;
    const title = clean(metadata?.title, 640);
    // Do not publish a generic page title as if it were the current song.
    const artwork = metadata?.artwork?.length ? metadata.artwork[metadata.artwork.length - 1].src : '';
    return { type: 'track', site: location.hostname, title, artist: clean(metadata?.artist, 640),
      artwork: clean(artwork, 4096), ready: !!audio || handlers.has('play') || !!playButton(), playing: audio ? !audio.paused && !audio.ended && (typeof audio.readyState !== 'number' || audio.readyState >= 2) : media?.playbackState === 'playing',
      volume: audio && Number.isFinite(audio.volume) ? (audio.muted ? 0 : audio.volume) : -1,
      position: audio && Number.isFinite(audio.currentTime) ? audio.currentTime : 0,
      duration: audio && Number.isFinite(audio.duration) && audio.duration > 0 && audio.duration < 604800 ? audio.duration : 0,
      rate: audio?.playbackRate || 1, canSeek: !!audio && Number.isFinite(audio.duration) && audio.duration > 0 && audio.duration < 604800,
      canPrevious: handlers.has('previoustrack'), canNext: handlers.has('nexttrack') };
  }
  function publish(force = false) {
    const data = snapshot(), json = JSON.stringify({...data, position: 0}), now = Date.now();
    if (force || json !== previous || now - lastSent > 15000) {
      previous = json; lastSent = now;
      window.postMessage({ source: 'IslandMusicPage', type: 'track', data }, location.origin);
    }
  }
  function watchAudio(audio) {
    lastAudio = audio;
    if (audio.__islandWatched) return;
    audio.__islandWatched = true;
    for (const name of ['play', 'pause', 'ended', 'loadedmetadata', 'emptied', 'volumechange', 'seeked', 'durationchange', 'ratechange']) audio.addEventListener(name, () => publish(true));
  }
  // Catch detached HTMLAudioElements too. No sound is captured or copied.
  for (const name of ['play', 'pause']) {
    const original = HTMLMediaElement.prototype[name];
    HTMLMediaElement.prototype[name] = function (...args) {
      watchAudio(this); const result = Reflect.apply(original, this, args);
      queueMicrotask(() => publish()); return result;
    };
  }
  if (media) {
    const original = media.setActionHandler.bind(media);
    media.setActionHandler = (action, callback) => {
      if (['play','pause','nexttrack','previoustrack'].includes(action)) {
        if (callback) handlers.set(action, callback); else handlers.delete(action);
        queueMicrotask(() => publish());
      }
      return original(action, callback);
    };
  }
  async function toggle(playOnly = false) {
    const data = snapshot();
    if (playOnly && data.playing) { publish(true); return true; }
    const action = playOnly ? 'play' : data.playing ? 'pause' : 'play';
    if (handlers.has(action)) await handlers.get(action)({ action });
    else {
      const audio = activeAudio();
      if (!audio) { const button = action === 'play' ? playButton() : null; if (!button) throw new Error('no_player'); button.click(); }
      else if (action === 'pause') audio.pause(); else await audio.play();
    }
    // Some players update MediaSession/audio state asynchronously. Observe the
    // result without issuing the action again (a retry could resume the song).
    for (let i = 0; i < 20; i++) {
      await new Promise(resolve => setTimeout(resolve, 80));
      if (snapshot().playing !== data.playing) { publish(true); return true; }
    }
    publish(true); return false;
  }
  async function skip(next) {
    const action = next ? 'nexttrack' : 'previoustrack';
    if (!handlers.has(action)) return false;
    const before = snapshot(), audio = activeAudio(), position = audio?.currentTime || 0;
    await handlers.get(action)({action});
    // Metadata may arrive after the player's asynchronous queue/network update.
    // Never resend the command while waiting: that could skip multiple tracks.
    for (let i = 0; i < 24; i++) {
      await new Promise(resolve => setTimeout(resolve, 80));
      const after = snapshot();
      if (after.title !== before.title || after.artist !== before.artist || after.artwork !== before.artwork
          || (!next && position > 2 && activeAudio()?.currentTime < 1)) {
        publish(true); return true;
      }
    }
    publish(true); return false;
  }
  window.addEventListener('message', async event => {
    if (event.source !== window || event.origin !== location.origin || event.data?.source !== 'IslandMusicContent') return;
    if (event.data.type === 'snapshot') publish(true);
    if (['toggle','play','next','previous','volume','seek'].includes(event.data.type) && typeof event.data.id === 'string') {
      let ok = false, reason = 'state_timeout';
      try {
        if (event.data.type === 'seek') {
          const audio = activeAudio(), value = event.data.position;
          if (!audio || !snapshot().canSeek) throw new Error('no_player');
          if (!Number.isFinite(value)) throw new Error('invalid_position');
          const target = Math.max(0, Math.min(audio.duration, value));
          audio.currentTime = target;
          for (let i = 0; i < 20; i++) {
            await new Promise(resolve => setTimeout(resolve, 80));
            if (Math.abs(audio.currentTime - target) < 2) { ok = true; break; }
          }
          publish(true);
        } else if (event.data.type === 'volume') {
          const audio = activeAudio(), value = event.data.volume;
          if (!audio) throw new Error('no_player');
          if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 1) throw new Error('invalid_volume');
          audio.volume = value; audio.muted = false; ok = Math.abs(audio.volume - value) < 0.015; publish(true);
        } else ok = ['toggle','play'].includes(event.data.type) ? await toggle(event.data.type === 'play') : await skip(event.data.type === 'next');
      }
      catch (error) { reason = error?.name === 'NotAllowedError' ? 'play_blocked' : error?.message === 'no_player' ? 'no_player' : 'player_error'; }
      window.postMessage({ source: 'IslandMusicPage', type: 'ack', id: event.data.id, ok, reason: ok ? '' : reason }, location.origin);
    }
  });
  // MediaSession has no metadata-changed event. Read only its small fields;
  // send over IPC only when changed, plus a sparse reconnect heartbeat.
  setInterval(publish, 1000);
})();
