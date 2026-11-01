const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const { test } = require("node:test");
const vm = require("node:vm");
const source = readFileSync(join(__dirname, "background.js"), "utf8");

function setup({ pending = { tabId: 1, url: "https://shop.example/product", expires: Date.now() + 60000 }, active = 1, tabURL = "https://shop.example/product" } = {}) {
  let listener;
  const state = { pendingImageSelection: pending }, writes = [];
  const chrome = {
    runtime: { id: "extension", onMessage: { addListener: (callback) => { listener = callback; } } },
    tabs: { get: async (id) => ({ id, url: tabURL }), query: async () => [{ id: active }] },
    storage: { session: {
      get: async (key) => ({ [key]: state[key] }),
      set: async (value) => { writes.push(value); Object.assign(state, value); },
      remove: async (key) => { delete state[key]; },
    } },
  };
  vm.runInNewContext(source, { chrome, URL, Date });
  const send = (message, sender = { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }) => new Promise((resolve) => {
    if (listener(message, sender, resolve) !== true) resolve(undefined);
  });
  return { state, writes, send };
}

test("accepts one explicitly selected public web image from the pending active product tab", async () => {
  const ui = setup();
  const expires = ui.state.pendingImageSelection.expires;
  const result = await ui.send({ action: "monkado-image-selected", image: "https://cdn.example/view.png" });
  assert.equal(result.ok, true);
  assert.equal(ui.state.manualImage.url, "https://shop.example/product");
  assert.equal(ui.state.manualImage.image, "https://cdn.example/view.png");
  assert.equal(ui.state.manualImage.tabId, 1); assert.equal(ui.state.manualImage.expires, expires);
  assert.equal(ui.state.pendingImageSelection, undefined);
  assert.equal((await ui.send({ action: "monkado-image-selected", image: "https://cdn.example/other.png" })).ok, false);
  assert.equal(ui.writes.length, 1);
});

test("rejects expired, unrelated, inactive and unsafe selections without storing an image", async () => {
  const cases = [
    [setup({ pending: null }), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup({ pending: { tabId: 1, url: "https://shop.example/product", expires: Date.now() - 1 } }), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup({ active: 3 }), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup({ tabURL: "https://shop.example/other" }), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup(), { id: "extension", frameId: 1, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup(), { id: "extension", frameId: 0, tab: { id: 2, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup(), { id: "other", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://cdn.example/view.png"],
    [setup(), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "javascript:alert(1)"],
    [setup(), { id: "extension", frameId: 0, tab: { id: 1, windowId: 2 } }, "https://user:password@cdn.example/view.png"],
  ];
  for (const [ui, sender, image] of cases) {
    const result = await ui.send({ action: "monkado-image-selected", image }, sender);
    assert.notEqual(result?.ok, true); assert.equal(ui.state.manualImage, undefined); assert.equal(ui.writes.length, 0);
  }
});

test("cancelling an active selection clears it without saving an image", async () => {
  const ui = setup();
  assert.equal((await ui.send({ action: "monkado-image-cancelled" })).ok, true);
  assert.equal(ui.state.pendingImageSelection, undefined); assert.equal(ui.state.manualImage, undefined); assert.equal(ui.writes.length, 0);
});
