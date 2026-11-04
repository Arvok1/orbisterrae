import assert from 'node:assert/strict';
import test from 'node:test';
import { Envelope } from '../dist/orbis.js';

test('shared Rust wire fixture re-encodes exactly', () => {
  const bytes = Uint8Array.from([8, 42, 18, 8, 10, 1, 120, 82, 3, 10, 1, 65]);
  const value = Envelope.decode(bytes);
  assert.equal(value.seq, 42n);
  assert.equal(value.body.$case, 'command');
  assert.equal(value.body.command.requestId, 'x');
  assert.equal(value.body.command.kind.foundCity.name, 'A');
  assert.deepEqual(Envelope.encode(value).finish(), bytes);
});

test('uint64 retains all bits and unicode survives round trip', () => {
  const value = Envelope.fromPartial({seq: 18446744073709551615n, body: {
    $case: 'command', command: { requestId: 'cidade', kind: {
      $case: 'foundCity', foundCity: {name: 'São Paulo', pos: {lat: -23.55, lon: -46.63}}
    }}
  }});
  assert.deepEqual(Envelope.decode(Envelope.encode(value).finish()), value);
});

test('truncated frame is rejected', () => {
  assert.throws(() => Envelope.decode(Uint8Array.from([255])));
});
