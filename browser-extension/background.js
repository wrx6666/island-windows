const tracks = new Map(), readyTabs = new Map(), readiness = new Set();
let nativePort = null, connected = false, selected = null, failure = '', lastAttempt = 0, lastTrack = '', launching = null;
const sites = new Set(['music.yandex.ru','music.yandex.com','vk.com','vk.ru','music.vk.com']);
const serviceOf = host => host === 'music.yandex.ru' || host === 'music.yandex.com' ? 'yandex' : sites.has(host) ? 'vk' : '';
function safeUrl(value, service) {
  if (!['yandex','vk'].includes(service)) return '';
  try { const u = new URL(value); if (u.protocol !== 'https:' || u.username || u.password || u.port || serviceOf(u.hostname) !== service) return '';
    if (service === 'vk' && u.hostname !== 'music.vk.com' && !/^\/(audio|music)(\/|$)/.test(u.pathname)) return 'https://vk.com/audio';
    return u.origin + u.pathname;
  } catch { return ''; }
}
function sendSelected() {
  if (!nativePort || !selected) return;
  const json = JSON.stringify(selected.data);
  if (json === lastTrack) return;
  nativePort.postMessage(selected.data); lastTrack = json;
}
function choose() {
  const values = [...tracks.values()];
  return values.filter(t => t.data.playing).sort((a,b) => b.when-a.when)[0]
    || values.find(t => t.tabId === selected?.tabId) || values.sort((a,b) => b.when-a.when)[0] || null;
}
function waitReady(id, timeout = 12000) {
  if (readyTabs.get(id)?.ready) return Promise.resolve(true);
  return new Promise(resolve => {
    const finish = () => { if (!readyTabs.get(id)?.ready) return; cleanup(); resolve(true); };
    const timer = setTimeout(() => { cleanup(); resolve(false); }, timeout);
    const cleanup = () => { clearTimeout(timer); readiness.delete(finish); };
    readiness.add(finish); finish();
  });
}
async function launchPlay(message) {
  if (launching) return launching;
  launching = (async () => {
    const url = safeUrl(message.url, message.service);
    if (!url) return {ok:false,reason:'unsupported'};
    if (typeof navigator !== 'undefined' && navigator.onLine === false) return {ok:false,reason:'offline'};
    const patterns = message.service === 'yandex' ? ['https://music.yandex.ru/*','https://music.yandex.com/*'] : ['https://vk.com/audio*','https://vk.com/music*','https://vk.ru/audio*','https://vk.ru/music*','https://music.vk.com/*'];
    const tabs = await chrome.tabs.query({url:patterns});
    const playing = [...tracks.values()].find(t => t.data.playing && serviceOf(t.data.site) === message.service);
    let tab = tabs.find(t => t.id === playing?.tabId) || tabs.find(t => t.id === selected?.tabId) || tabs[0];
    if (!tab) tab = await chrome.tabs.create({url,active:false});
    // Ask for one snapshot on reused pages; all subsequent readiness updates are events.
    try { await chrome.tabs.sendMessage(tab.id,{type:'snapshot'}); } catch { }
    if (!await waitReady(tab.id)) return {ok:false,reason:'player_not_ready'};
    if (tracks.has(tab.id)) { selected = tracks.get(tab.id); sendSelected(); }
    try { return await chrome.tabs.sendMessage(tab.id,{type:'play'}); } catch { return {ok:false,reason:'tab_disconnected'}; }
  })();
  try { return await launching; } finally { launching = null; }
}
async function discoverTabs() {
  const tabs = await chrome.tabs.query({url:['https://music.yandex.ru/*','https://music.yandex.com/*','https://vk.com/audio*','https://vk.com/music*','https://vk.ru/audio*','https://vk.ru/music*','https://music.vk.com/*']});
  for (const tab of tabs) { try { await chrome.tabs.sendMessage(tab.id,{type:'snapshot'}); } catch { } }
  return tabs;
}
function connect(probe = false) {
  if (nativePort || !selected && !probe || failure && Date.now() - lastAttempt < 3000) return;
  lastAttempt = Date.now(); failure = '';
  try {
    const port = chrome.runtime.connectNative('com.desktopisland.music'); nativePort = port; lastTrack = '';
    port.onMessage.addListener(async message => {
      if (message.type === 'status') { connected = message.connected === true; failure = ''; if (connected) { sendSelected(); discoverTabs().catch(() => {}); } }
      if (message.type === 'launch_play' || ['toggle','play','next','previous','volume','seek'].includes(message.type)) {
        let reply = {ok:false,reason:'no_tab'};
        try {
          if (message.type === 'launch_play') reply = await launchPlay(message);
          else {
            // Rebuild routing after a worker restart without waiting for track
            // metadata or routing a browser command through stale GSMTC.
            const target = selected || (await discoverTabs())[0];
            const id = target?.tabId ?? target?.id;
            if (id != null) reply = await chrome.tabs.sendMessage(id,{type:message.type,volume:message.volume,position:message.position});
          }
        } catch { reply = {ok:false,reason:'tab_disconnected'}; }
        if (nativePort === port) port.postMessage({type:'ack',id:message.id,ok:reply?.ok === true,reason:reply?.ok ? '' : reply?.reason || 'state_timeout'});
      }
    });
    port.onDisconnect.addListener(() => {
      if (nativePort === port) { failure = chrome.runtime.lastError?.message || 'Island отключён'; nativePort = null; connected = false; lastTrack = ''; }
    });
    port.postMessage({type:'bridge',version:1});
    sendSelected();
  } catch (error) { failure = String(error); nativePort = null; connected = false; }
}
function update() {
  const prior = selected; selected = choose();
  if (selected) { connect(); sendSelected(); }
  else if (prior && nativePort) {
    nativePort.postMessage({type:'track',site:prior.data.site,title:'',artist:'',artwork:'',playing:false}); lastTrack = '';
  }
}
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (message.type === 'status' && !sender.tab) {
    connect(true); respond({connected,connecting:!!nativePort && !connected && !failure,title:selected?.data.title || '',artist:selected?.data.artist || '',failure}); return false;
  }
  if (message.type !== 'track' || sender.tab?.id == null) return false;
  let site; try { site = new URL(sender.url).hostname; } catch { return false; }
  if (!sites.has(site) || message.site !== site) return false;
  const data = {...message,pageUrl:safeUrl(sender.url,serviceOf(site))};
  readyTabs.set(sender.tab.id,{ready:message.ready === true});
  for (const notify of [...readiness]) notify();
  if (!message.title) tracks.delete(sender.tab.id);
  else tracks.set(sender.tab.id,{tabId:sender.tab.id,when:Date.now(),data});
  update(); return false;
});
chrome.tabs.onRemoved.addListener(id => { readyTabs.delete(id); tracks.delete(id); update(); });
chrome.tabs.onUpdated.addListener((id,change) => { if (change.status === 'loading') { readyTabs.delete(id); if (tracks.delete(id)) update(); } });
chrome.runtime.onStartup.addListener(() => connect(true));
chrome.runtime.onInstalled.addListener(() => connect(true));
// One native connection for this browser, including when no music tab exists yet.
// No page is opened and no browser is started by this handshake.
connect(true);
