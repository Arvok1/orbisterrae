import assert from 'node:assert/strict';
import test from 'node:test';
import {sceneBlend} from '../dist/map/scene.js';

test('map and close-view transition has bounded, monotonic endpoints', () => {
  assert.equal(sceneBlend(0,true),0);
  assert.equal(sceneBlend(6,true),1);
  assert.equal(sceneBlend(6,false),0);
  assert.equal(sceneBlend(NaN,true),0);
  let previous = 0;
  for (let zoom=0; zoom<=6; zoom+=0.01) {
    const current = sceneBlend(zoom,true);
    assert.ok(current>=previous && current>=0 && current<=1);
    previous=current;
  }
});
