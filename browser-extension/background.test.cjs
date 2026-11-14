const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function bridge() {
  let runtimeListener, opens=0, tabs=[], creates=0, replies={ok:false,reason:"play_blocked"};
  const nativeMessages=[], posted=[], disconnects=[], lifecycle=[], network={onLine:true};
  const chrome={runtime:{onStartup:{addListener:cb=>lifecycle.push(cb)},onInstalled:{addListener:cb=>lifecycle.push(cb)},connectNative(name){assert.equal(name,'com.desktopisland.music'); opens++; return {postMessage:data=>posted.push(data),onMessage:{addListener:cb=>nativeMessages.push(cb)},onDisconnect:{addListener:cb=>disconnects.push(cb)},disconnect(){disconnects.at(-1)()}}},onMessage:{addListener:cb=>runtimeListener=cb}},tabs:{onRemoved:{addListener(){}},onUpdated:{addListener(){}},async query(){return tabs},async create(options){creates++; const tab={id:9,...options};tabs.push(tab);return tab},async sendMessage(){return replies}}};
  vm.runInNewContext(fs.readFileSync(__dirname+'/background.js','utf8'),{chrome,URL,setTimeout,clearTimeout,navigator:network});
  return {chrome,posted,startup(){for(const callback of lifecycle)callback()},setOffline(){network.onLine=false},setTabs(value){tabs=value},setReply(value){replies=value},get creates(){return creates},get opens(){return opens},status(){let result;runtimeListener({type:'status'},{},value=>result=value);return result},track(data,id=7,url="https://music.yandex.ru/"){runtimeListener({type:'track',site:'music.yandex.ru',title:'Трек',artist:'',playing:true,...data},{tab:{id},url},()=>{})},async native(data){for(const callback of nativeMessages) await callback(data)},disconnect(){disconnects.at(-1)()}};
}
test('connection can be checked before any track metadata exists',async()=>{
  const b=bridge(); assert.equal(b.status().connecting,true); assert.equal(b.opens,1);
  await b.native({type:'status',connected:true});
  assert.equal(b.status().connected,true); assert.equal(b.status().title,''); assert.equal(b.opens,1);
});
test('native host error is exposed and repeated popup polls do not relaunch it',()=>{
  const b=bridge(); b.status(); b.chrome.runtime.lastError={message:'Specified native messaging host not found.'}; b.disconnect();
  assert.match(b.status().failure,/not found/); assert.equal(b.opens,1);
});
test('player rejection reason survives the extension relay',async()=>{
  const b=bridge(); b.track(); await b.native({type:'toggle',id:11});
  assert.deepEqual(JSON.parse(JSON.stringify(b.posted.at(-1))),{type:'ack',id:11,ok:false,reason:'play_blocked'});
});


test('initial track and identical heartbeat are sent once, reconnect status does not duplicate',async()=>{
  const b=bridge(); b.track({position:84,duration:236});
  assert.equal(b.posted.filter(x=>x.type==='track').length,1);
  await b.native({type:'status',connected:true});
  assert.equal(b.posted.filter(x=>x.type==='track').length,1);
  b.track({position:84,duration:236}); assert.equal(b.posted.filter(x=>x.type==='track').length,1);
  b.track({position:99,duration:236}); assert.equal(b.posted.filter(x=>x.type==='track').length,2);
});


test('bridge announces launch capability without a music tab',()=>{
 const b=bridge();assert.deepEqual(JSON.parse(JSON.stringify(b.posted[0])),{type:'bridge',version:1});assert.equal(b.creates,0);
});
test('launch reuses an existing tab, sends idempotent play and returns rejection',async()=>{
 const b=bridge();b.setTabs([{id:7,url:'https://music.yandex.ru/'}]);b.track({ready:true,playing:false});
 await b.native({type:'launch_play',service:'yandex',url:'https://music.yandex.ru/',id:20});
 assert.equal(b.creates,0);assert.equal(b.posted.at(-1).reason,'play_blocked');
});
test('simultaneous Play opens only one inactive tab and waits for readiness',async()=>{
 const b=bridge();b.setReply({ok:true});
 const a=b.native({type:'launch_play',service:'yandex',url:'https://music.yandex.ru/',id:21});
 const c=b.native({type:'launch_play',service:'yandex',url:'https://music.yandex.ru/',id:22});
 await new Promise(resolve=>setImmediate(resolve));assert.equal(b.creates,1);
 b.track({ready:true,playing:true},9);await Promise.all([a,c]);
 assert.equal(b.posted.filter(x=>x.type==='ack'&&x.ok).length,2);
});
test('untrusted URL cannot create tabs',async()=>{
 const b=bridge();await b.native({type:'launch_play',service:'yandex',url:'https://example.com/',id:23});assert.equal(b.creates,0);assert.equal(b.posted.at(-1).reason,'unsupported');
});
test('readiness without track metadata keeps the launch connection alive',()=>{
 const b=bridge();b.track({title:'',ready:true,playing:false});assert.equal(b.opens,1);assert.equal(b.status().connecting,true);
});


test('offline state returns an error without creating a tab',async()=>{
 const b=bridge();b.setOffline();await b.native({type:'launch_play',service:'yandex',url:'https://music.yandex.ru/',id:24});assert.equal(b.creates,0);assert.equal(b.posted.at(-1).reason,'offline');
});
test('saved URLs are stripped of query tokens before opening',async()=>{
 const b=bridge();b.setReply({ok:true});const job=b.native({type:'launch_play',service:'yandex',url:'https://music.yandex.ru/album/123?token=secret#position',id:25});
 await new Promise(resolve=>setImmediate(resolve));b.track({ready:true},9);await job;
 const tabs=await b.chrome.tabs.query({});assert.equal(tabs[0].url,'https://music.yandex.ru/album/123');assert.equal(tabs[0].active,false);
});


test('browser startup and extension lifecycle keep a single native port',()=>{
 const b=bridge();b.startup();assert.equal(b.opens,1);assert.equal(b.posted.filter(x=>x.type==='bridge').length,1);
});
