import assert from "node:assert/strict";
import test from "node:test";
import { clientPackageName } from "../dist/main.js";

test("client workspace is executable", () => {
  assert.equal(clientPackageName, "@orbis/client");
});
