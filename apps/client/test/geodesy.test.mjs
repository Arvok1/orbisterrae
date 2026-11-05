import assert from 'node:assert/strict';
import test from 'node:test';
import { latLonToLocalMeters, latLonToUnit, localMetersToLatLon, unitToLatLon } from '../dist/map/geodesy.js';

test('geodesic unit vector round-trips latitude and longitude',()=>{
  const source={lat:-23.5505,lon:-46.6333};
  const result=unitToLatLon(latLonToUnit(source));
  assert.ok(Math.abs(result.lat-source.lat)<1e-9);
  assert.ok(Math.abs(result.lon-source.lon)<1e-9);
});

test('local city metre frame preserves a physical round-trip',()=>{
  const origin={lat:48.8566,lon:2.3522},metres={east:1_280,north:-1_280};
  const result=latLonToLocalMeters(origin,localMetersToLatLon(origin,metres));
  assert.ok(Math.abs(result.east-metres.east)<.001);
  assert.ok(Math.abs(result.north-metres.north)<.001);
});
