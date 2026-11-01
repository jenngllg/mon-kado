const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const { test } = require("node:test");
const vm = require("node:vm");
const source = readFileSync(join(__dirname, "apply.js"), "utf8");

function setup({ existingLink = "", existingName = "", existingPrice = "", locked = false, wrongStep = false, supported = true, accepted = true } = {}) {
  const events = [];
  const input = (name, value) => ({ name, value, disabled: locked, dispatchEvent: (event) => events.push([name, event.type]) });
  const link = input("url", existingLink), name = input("name", existingName), price = input("price", existingPrice);
  const form = { hidden: locked, querySelector: (selector) => selector.includes('"name"') ? name : price };
  const analysis = { dataset: { directImportPoc: supported ? "1" : "" }, closest: () => ({ hidden: wrongStep }), querySelector: () => link, dispatchEvent: (event) => {
    events.push(["analysis", event.type]); if (!accepted) return true;
    link.value = event.detail.url; if (!name.value) name.value = event.detail.name; if (!price.value) price.value = event.detail.price; return false;
  } };
  const document = { querySelector: (selector) => selector.includes('form[aria-label="Ajouter') ? form : analysis };
  const context = { document, Event: class { constructor(type) { this.type = type; } }, CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } } };
  vm.runInNewContext(source, context);
  return { link, name, price, events, apply: context.monKadoApplySuggestion };
}
const suggestion = { url: "https://shop.example/product", name: "Gift", price: "16,95" };

test("fills an empty creation form and notifies the application", () => {
  const ui = setup();
  assert.match(ui.apply(suggestion).message, /Champs remplis/);
  assert.equal(ui.link.value, suggestion.url); assert.equal(ui.name.value, suggestion.name); assert.equal(ui.price.value, suggestion.price);
  assert.deepEqual(ui.events, [["analysis", "monkado-direct-import"]]);
});

test("preserves manually entered values", () => {
  const ui = setup({ existingName: "Mine", existingPrice: "7" }); ui.apply(suggestion);
  assert.equal(ui.name.value, "Mine"); assert.equal(ui.price.value, "7"); assert.deepEqual(ui.events, [["analysis", "monkado-direct-import"]]);
});

test("does not trigger legacy analysis when the direct bridge is unavailable or refuses data", () => {
  const unsupported = setup({ supported: false }); assert.match(unsupported.apply(suggestion).message, /ne prend pas/); assert.deepEqual(unsupported.events, []);
  const refused = setup({ accepted: false }); assert.match(refused.apply(suggestion).message, /refusé/); assert.equal(refused.link.value, "");
});

test("does not change a form for another product or a locked form", () => {
  const other = setup({ existingLink: "https://other.example/" });
  assert.match(other.apply(suggestion).message, /autre lien/); assert.deepEqual(other.events, []);
  const locked = setup({ locked: true }); assert.match(locked.apply(suggestion).message, /étape du lien/); assert.deepEqual(locked.events, []);
  const wrongStep = setup({ wrongStep: true }); assert.match(wrongStep.apply(suggestion).message, /étape du lien/); assert.deepEqual(wrongStep.events, []);
});

function imageSetup({ locked = false, existing = false, link = suggestion.url } = {}) {
  const events = [];
  const input = { disabled: locked, files: null, dispatchEvent: (event) => events.push(event.type) };
  const remove = { hidden: !existing };
  const panel = { querySelector: (selector) => selector.includes('type="file"') ? input : remove };
  const form = { hidden: locked };
  const context = {
    document: { querySelector: (selector) => selector.includes('form[aria-label="Ajouter') ? form : selector.includes('Analyser un lien produit') ? link === null ? null : { value: link } : panel },
    Event: class { constructor(type) { this.type = type; } },
    File: class { constructor(parts, name, options) { this.parts = parts; this.name = name; this.type = options.type; } },
    DataTransfer: class { constructor() { this.files = []; this.items = { add: (file) => this.files.push(file) }; } },
    Uint8Array, atob,
  };
  vm.runInNewContext(source, context);
  return { input, events, apply: context.monKadoApplyImage };
}

test("transfers a copied image as a file for the application's validation", () => {
  const ui = imageSetup();
  assert.match(ui.apply({ type: "image/png", encoded: "aGVsbG8=" }).message, /Image transmise/);
  assert.equal(ui.input.files[0].type, "image/png"); assert.deepEqual(ui.events, ["change"]);
});

test("never replaces a selected image or injects into a locked form", () => {
  const existing = imageSetup({ existing: true });
  assert.match(existing.apply({ type: "image/png", encoded: "aGVsbG8=" }).message, /déjà sélectionnée/);
  assert.deepEqual(existing.events, []);
  const locked = imageSetup({ locked: true });
  assert.match(locked.apply({ type: "image/png", encoded: "aGVsbG8=" }).message, /Attends/);
  assert.deepEqual(locked.events, []);
});

test("transfers a retained image only for the matching form link", () => {
  const image = { type: "image/png", encoded: "aGVsbG8=", expectedURL: suggestion.url };
  for (const link of ["https://other.example/", null]) {
    const ui = imageSetup({ link });
    assert.match(ui.apply(image).message, /ne correspond pas/);
    assert.deepEqual(ui.events, []);
  }
  const matching = imageSetup();
  assert.match(matching.apply(image).message, /Image transmise/);
  assert.deepEqual(matching.events, ["change"]);
});

test("rejects unsupported or malformed copied images before notifying the application", () => {
  const ui = imageSetup();
  assert.match(ui.apply({ type: "image/svg+xml", encoded: "aGVsbG8=" }).message, /invalide/);
  assert.match(ui.apply({ type: "image/png", encoded: "???" }).message, /Impossible/);
  assert.deepEqual(ui.events, []);
});
