import assert from 'node:assert/strict';
import { BotClient } from './client.js';

const base = process.env.ORBIS_HTTP_URL ?? 'http://127.0.0.1:8080';
const ws = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
const username = process.env.ORBIS_ADMIN_TEST_USER;
const password = process.env.ORBIS_ADMIN_TEST_PASSWORD;
const expectAdmin = process.env.ORBIS_EXPECT_ADMIN === 'true';
assert.ok(username && password, 'Admin smoke credentials are required');

const response = await fetch(`${base}/api/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password }),
});
assert.equal(response.status, 200, await response.clone().text());
const session = await response.json() as { token: string };
const client = await BotClient.open(ws, 10_000);
try {
  const authentication = await client.request({ $case: 'authenticate', authenticate: { token: session.token } });
  assert.equal(authentication.kind?.$case, 'result');
  if (authentication.kind?.$case !== 'result') throw new Error('Expected authentication result');
  assert.ok(authentication.kind.result.ok, authentication.kind.result.error);
  const snapshot = await client.request({
    $case: 'adminSnapshot',
    adminSnapshot: {
      afterEventSeq: 0n, accountId: '', kingdomId: '', cityId: '', shard: 0n,
      eventKind: 0, filterShard: false, filterEventKind: false, includeWorld: true,
    },
  });
  if (expectAdmin) {
    assert.equal(snapshot.kind?.$case, 'admin');
  } else {
    assert.equal(snapshot.kind?.$case, 'result');
    if (snapshot.kind?.$case === 'result') assert.equal(snapshot.kind.result.error, 'admin_required');
  }
  console.log(JSON.stringify({ result: 'passed', expectAdmin, response: snapshot.kind?.$case }));
} finally {
  client.close();
}
