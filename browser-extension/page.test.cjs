const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

function player() {
  const messages=[], listeners=[], timers=[];
  class Audio {
    constructor() { this.duration=236; this.currentTime=0; this.playbackRate=1; this.paused=true; this.ended=false; this.volume=1; this.muted=false; this.events={}; }
    addEventListener(name, cb) { (this.events[name]??=[]).push(cb); }
    play() { this.paused=false; for(const cb of this.events.play||[]) cb(); return Promise.resolve(); }
    pause() { this.paused=true; for(const cb of this.events.pause||[]) cb(); }
  }
  const audio=new Audio();
  const media={metadata:{title:'Первый трек',artist:'Исполнитель',artwork:[{src:'https://avatars.yandex.net/cover'}]},playbackState:'none',setActionHandler(){}};
  const window={addEventListener(name,cb){listeners.push(cb)},postMessage(data){messages.push(data)}};
  const context={window,navigator:{mediaSession:media},location:{hostname:'music.yandex.ru',origin:'https://music.yandex.ru'},document:{querySelectorAll:()=>[]},HTMLMediaElement:Audio,queueMicrotask,setTimeout,setInterval:cb=>timers.push(cb)};
  vm.runInNewContext(fs.readFileSync(__dirname+'/page.js','utf8'),context);
  return {audio,media,messages,timers,async toggle(id,type='toggle',volume,position){for(const cb of listeners) await cb({source:window,origin:context.location.origin,data:{source:'IslandMusicContent',type,id,volume,position}})}};
}
test('detached audio, metadata, pause/resume and new cover',async()=>{
  const p=player(); await p.audio.play(); await Promise.resolve();
  let track=p.messages.filter(x=>x.type==='track').at(-1).data;
  assert.equal(track.title,'Первый трек'); assert.equal(track.playing,true);
  await p.toggle('pause'); assert.equal(p.audio.paused,true); assert.equal(p.messages.at(-1).ok,true);
  await p.toggle('resume'); assert.equal(p.audio.paused,false); assert.equal(p.messages.at(-1).ok,true);
  p.media.metadata={title:'Второй трек',artist:'Другой',artwork:[{src:'https://avatars.yandex.net/new'}]};
  p.timers[0](); track=p.messages.filter(x=>x.type==='track').at(-1).data;
  assert.equal(track.title,'Второй трек'); assert.equal(track.artwork,'https://avatars.yandex.net/new');
});
test('site handlers and missing metadata',async()=>{
  const p=player(); await p.audio.play();
  let called=0; p.media.setActionHandler('pause',()=>{called++; p.audio.pause()});
  await p.toggle('handled'); assert.equal(called,1); assert.equal(p.messages.at(-1).ok,true);
  p.media.metadata=null; p.timers[0](); assert.equal(p.messages.filter(x=>x.type==='track').at(-1).data.title,'');
});
test('failed player command has negative acknowledgement',async()=>{
  const p=player(); await p.audio.play(); p.media.setActionHandler('pause',()=>{});
  await p.toggle('failed'); assert.equal(p.messages.at(-1).ok,false);
});
test('next and previous call the correct handler once and verify new metadata',async()=>{
  const p=player(); await p.audio.play(); let next=0, previous=0;
  p.media.setActionHandler('nexttrack',()=>{next++; p.media.metadata.title='Второй трек'});
  p.media.setActionHandler('previoustrack',()=>{previous++; p.media.metadata.title='Первый трек'});
  await p.toggle('next','next'); assert.equal(p.messages.at(-1).ok,true); assert.equal(next,1);
  await p.toggle('previous','previous'); assert.equal(p.messages.at(-1).ok,true); assert.equal(previous,1);
  const track=p.messages.filter(x=>x.type==='track').at(-1).data;
  assert.equal(track.canPrevious,true); assert.equal(track.canNext,true);
});
test('unavailable skip never falls back to toggling play',async()=>{
  const p=player(); await p.audio.play(); await p.toggle('absent','next');
  assert.equal(p.messages.at(-1).ok,false); assert.equal(p.audio.paused,false);
});
test('previous may restart the current track; delayed track changes are awaited',async()=>{
  const p=player(); await p.audio.play(); p.audio.currentTime=25;
  p.media.setActionHandler('previoustrack',()=>{p.audio.currentTime=0});
  await p.toggle('restart','previous'); assert.equal(p.messages.at(-1).ok,true);
  let calls=0;
  p.media.setActionHandler('nexttrack',()=>{calls++; setTimeout(()=>{p.media.metadata.title='Следующий'},150)});
  await p.toggle('delayed','next'); assert.equal(p.messages.at(-1).ok,true); assert.equal(calls,1);
});
test('delayed pause is observed without sending pause a second time',async()=>{
  const p=player(); await p.audio.play(); let calls=0;
  p.media.setActionHandler('pause',()=>{calls++; setTimeout(()=>p.audio.pause(),220)});
  await p.toggle('slow'); assert.equal(p.messages.at(-1).ok,true); assert.equal(calls,1);
});
test('missing player and autoplay rejection have distinct reasons',async()=>{
  const absent=player(); await absent.toggle('absent');
  assert.equal(absent.messages.at(-1).reason,'no_player');
  const blocked=player(); blocked.media.setActionHandler('play',()=>{throw {name:'NotAllowedError'}});
  await blocked.toggle('blocked'); assert.equal(blocked.messages.at(-1).reason,'play_blocked');
});


test('volume changes the selected music element without changing playback',async()=>{
  const p=player(); await p.audio.play();
  await p.toggle('quiet','volume',0.27);
  assert.equal(p.messages.at(-1).ok,true); assert.equal(p.audio.volume,0.27); assert.equal(p.audio.paused,false);
  let track=p.messages.filter(x=>x.type==='track').at(-1).data; assert.equal(track.volume,0.27);
  await p.toggle('mute','volume',0); assert.equal(p.audio.volume,0); assert.equal(p.audio.paused,false);
  await p.toggle('invalid','volume',2); assert.equal(p.messages.at(-1).ok,false); assert.equal(p.audio.volume,0);
});


test('timeline follows selected audio, seek clamps and elapsed time does not flood IPC',async()=>{
  const p=player(); await p.audio.play(); await Promise.resolve(); p.audio.currentTime=84;
  p.timers[0]();
  let track=p.messages.filter(x=>x.type==='track').at(-1).data;
  assert.equal(track.duration,236); assert.equal(track.canSeek,true);
  const count=p.messages.length; p.audio.currentTime=85; p.timers[0](); assert.equal(p.messages.length,count);
  await p.toggle('seek','seek',undefined,110); assert.equal(p.audio.currentTime,110); assert.equal(p.messages.at(-1).ok,true); assert.equal(p.audio.paused,false);
  track=p.messages.filter(x=>x.type==='track').at(-1).data; assert.equal(track.position,110);
  await p.toggle('end','seek',undefined,999); assert.equal(p.audio.currentTime,236);
  p.audio.duration=Infinity; await p.toggle('live','seek',undefined,10); assert.equal(p.messages.at(-1).ok,false);
});


test('explicit Play is idempotent when already playing',async()=>{
 const p=player();await p.audio.play();let calls=0;p.media.setActionHandler('play',()=>{calls++;p.audio.play()});
 await p.toggle('play','play');assert.equal(p.audio.paused,false);assert.equal(calls,0);assert.equal(p.messages.at(-1).ok,true);
 p.audio.pause();await p.toggle('resume','play');assert.equal(calls,1);assert.equal(p.audio.paused,false);assert.equal(p.messages.at(-1).ok,true);
});


test('buffering media is not reported as confirmed playback',async()=>{
 const p=player();p.audio.readyState=0;await p.audio.play();await Promise.resolve();
 assert.equal(p.messages.filter(x=>x.type==='track').at(-1).data.playing,false);
 setTimeout(()=>{p.audio.readyState=3},150);await p.toggle('buffer','play');assert.equal(p.messages.at(-1).ok,true);
});
