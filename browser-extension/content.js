(() => {
  const pending = new Map();
  let valid = true;
  window.addEventListener('message', event => {
    if (event.source !== window || event.origin !== location.origin || event.data?.source !== 'IslandMusicPage') return;
    const message = event.data;
    if (message.type === 'track' && valid) {
      const d = message.data;
      if (!d || d.site !== location.hostname || typeof d.title !== 'string') return;
      chrome.runtime.sendMessage({ type: 'track', site: location.hostname,
        title: d.title.slice(0,640), artist: String(d.artist || '').slice(0,640),
        artwork: String(d.artwork || '').slice(0,4096), playing: d.playing === true, ready: d.ready === true,
        volume: Number.isFinite(d.volume) && d.volume >= 0 && d.volume <= 1 ? d.volume : -1,
        position: Number.isFinite(d.position) && d.position >= 0 ? d.position : 0,
        duration: Number.isFinite(d.duration) && d.duration > 0 && d.duration < 604800 ? d.duration : 0,
        rate: Number.isFinite(d.rate) && d.rate > 0 && d.rate <= 16 ? d.rate : 1, canSeek: d.canSeek === true,
        canPrevious: d.canPrevious === true, canNext: d.canNext === true }).catch(error => {
          if (String(error).includes('Extension context invalidated')) valid = false;
        });
    }
    if (message.type === 'ack' && pending.has(message.id)) {
      const done = pending.get(message.id); pending.delete(message.id); done({ok: message.ok === true, reason: typeof message.reason === 'string' ? message.reason.slice(0,48) : ''});
    }
  });
  chrome.runtime.onMessage.addListener((message, sender, respond) => {
    if (message.type === 'snapshot') { window.postMessage({source:'IslandMusicContent',type:'snapshot'},location.origin); respond({ok:true}); return false; }
    if (!['toggle','play','next','previous','volume','seek'].includes(message.type)) return false;
    const id = crypto.randomUUID();
    const timeout = setTimeout(() => { pending.delete(id); respond({ok:false,reason:'page_timeout'}); }, 2800);
    pending.set(id, result => { clearTimeout(timeout); respond(result); });
    window.postMessage({ source: 'IslandMusicContent', type: message.type, volume: message.volume, position: message.position, id }, location.origin);
    return true;
  });
  window.postMessage({source:'IslandMusicContent',type:'snapshot'}, location.origin);
})();
