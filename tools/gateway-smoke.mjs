import assert from 'node:assert/strict';
import { Envelope } from '../packages/protocol/dist/orbis.js';

const base = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
const count = Number(process.env.ORBIS_CONNECTIONS ?? 1000);
assert.ok(Number.isInteger(count) && count > 0 && count <= 1000);
const sockets = [];
function open() {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(base);
    socket.binaryType = 'arraybuffer';
    const timeout = setTimeout(() => { socket.close(); reject(new Error('WS open timeout')); }, 15000);
    socket.addEventListener('open', () => {clearTimeout(timeout); sockets.push(socket); resolve(socket);}, {once:true});
    socket.addEventListener('error', () => {clearTimeout(timeout); reject(new Error('WS connection failed'));}, {once:true});
  });
}
function request(socket, viewport, seq = 1n, kind = undefined) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Reply timeout')), 15000);
    socket.addEventListener('message', event => {
      clearTimeout(timeout);
      try { resolve(Envelope.decode(new Uint8Array(event.data))); } catch (e) { reject(e); }
    }, {once: true});
    socket.send(Envelope.encode(Envelope.fromPartial({seq, body: {$case:'command', command:{
      requestId: 'smoke', kind: kind ?? {$case:'viewport',viewport}
    }}})).finish());
  });
}
try {
  const first = await open();
  const full = {center:{lat:0,lon:0},zoom:0,widthPx:1024,heightPx:768};
  const response = await request(first, full);
  assert.equal(response.body.server.kind.$case, 'snapshot');
  const entities = response.body.server.kind.snapshot.entities;
  assert.equal(entities.length, 10000);
  assert.equal(new Set(entities.map(e => e.id)).size, 10000);
  const invalid = await request(first, {...full,zoom:99},2n);
  assert.equal(invalid.body.server.kind.result.error, 'invalid_viewport');
  const unsupported = await request(first, full,3n,{$case:'foundCity',foundCity:{name:'Test'}});
  assert.equal(unsupported.body.server.kind.result.ok,false);
  for (let index = 1; index < count; index += 50) {
    await Promise.all(Array.from({length:Math.min(50,count-index)},open));
  }
  const started = performance.now();
  await Promise.all(sockets.map((socket,index) => request(socket,{...full,zoom:6},BigInt(index+10)).then(reply=>{
    assert.equal(reply.seq,BigInt(index+10));
    assert.equal(reply.body.server.kind.$case,'snapshot');
    assert.ok(reply.body.server.kind.snapshot.entities.length<100);
  })));
  console.log(JSON.stringify({connections:sockets.length,fullSnapshot:entities.length,
    simultaneousRequestsMs:Math.round(performance.now()-started),result:'passed'}));
} finally {
  for (const socket of sockets) socket.close();
}
