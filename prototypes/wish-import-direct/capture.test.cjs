const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const { test } = require("node:test");
const vm = require("node:vm");
const source = readFileSync(join(__dirname, "capture.js"), "utf8");
const limitsSource = readFileSync(join(__dirname, "capture-limits.js"), "utf8");

function setup({ rect = { left: 20, top: 30, right: 220, bottom: 230, width: 200, height: 200 }, covered = false, src = "https://cdn.example/product.png", visible = true } = {}) {
  const img = { naturalWidth: 500, naturalHeight: 500, currentSrc: src, getBoundingClientRect: () => rect };
  const document = { baseURI: "https://shop.example/", images: [img], elementFromPoint: () => covered ? {} : img };
  const context = { document, window: { innerWidth: 500, innerHeight: 500 }, URL, getComputedStyle: () => ({ visibility: visible ? "visible" : "hidden", opacity: "1" }) };
  vm.runInNewContext(source, context);
  return (url) => JSON.parse(JSON.stringify(context.monKadoVisibleImage(url)));
}

test("locates only a fully visible image matching the detected URL", () => {
  const locate = setup();
  assert.deepEqual(locate("https://cdn.example/product.png"), { x: 20, y: 30, width: 200, height: 200, viewportWidth: 500, viewportHeight: 500 });
  assert.equal(locate("https://cdn.example/other.png"), null);
  assert.equal(locate("javascript:alert(1)"), null);
});

test("rejects images outside the viewport, hidden or covered by an element", () => {
  const imageURL = "https://cdn.example/product.png";
  assert.equal(setup({ covered: true })(imageURL), null);
  assert.equal(setup({ visible: false })(imageURL), null);
  assert.equal(setup({ rect: { left: -1, top: 0, right: 199, bottom: 200, width: 200, height: 200 } })(imageURL), null);
  assert.equal(setup({ rect: { left: 0, top: 0, right: 90, bottom: 90, width: 90, height: 90 } })(imageURL), null);
});

test("recognizes a resized image only for a hash-based filename, origin and query", () => {
  const filename = "eb5246f69483e4d94bb78b204997f54491c072ac_41003183.jpeg";
  const locate = setup({ src: `https://cdn.example/463/429/${filename}?v=1` });
  assert.ok(locate(`https://cdn.example/0/0/${filename}?v=1`));
  assert.equal(locate(`https://cdn.example/0/0/${filename}?v=2`), null);
  assert.equal(locate(`https://other.example/0/0/${filename}?v=1`), null);
  assert.equal(setup({ src: "https://cdn.example/large/item.jpg" })("https://cdn.example/small/item.jpg"), null);
  assert.equal(setup({ src: "https://cdn.example/large/an-identical-long-product-filename.jpg" })("https://cdn.example/small/an-identical-long-product-filename.jpg"), null);
});

test("matches Fnac image resolutions only for the same product and gallery view", () => {
  const base = "https://static.fnac-static.com/multimedia/Images/FR/MDM/9d/b6/5b/22787741";
  const filename = "tsp20260918073754/Figurine-Funko-Pop-TV-Wednesday-Rave-n-W.jpg";
  const locate = setup({ src: `${base}/1540-1/${filename}` });
  assert.ok(locate(`${base}/3756-1/${filename}`));
  assert.equal(locate(`${base}/3756-2/${filename}`), null);
  assert.equal(locate(`${base.replace("22787741", "22787742")}/3756-1/${filename}`), null);
  assert.equal(locate(`${base}/3756-1/${filename}?variant=other`), null);
  assert.equal(locate(`${base}/3756-1/${filename.replace("20260918073754", "20260919073754")}`), null);
});

test("matches only the same responsive asset and preserves variant-bearing parameters", () => {
  const cultura = setup({ src: "https://cdn.cultura.com/cdn-cgi/image/width=830/media/pim/14_4079935_1_10_FR.jpg" });
  assert.ok(cultura("https://cdn.cultura.com/cdn-cgi/image/width=250/media/pim/14_4079935_1_10_FR.jpg"));
  assert.equal(cultura("https://cdn.cultura.com/cdn-cgi/image/width=250/media/pim/28_4079935_2_20_FR.jpg"), null);
  const shopify = setup({ src: "https://www.tediber.com/cdn/shop/files/pillow.jpg?v=1&width=800" });
  assert.ok(shopify("https://www.tediber.com/cdn/shop/files/pillow.jpg?v=1"));
  assert.equal(shopify("https://www.tediber.com/cdn/shop/files/pillow.jpg?v=2"), null);
  const perfume = setup({ src: "https://media.marionnaud.fr/medias/abc/prd-front-100257559-150x150.png" });
  assert.ok(perfume("https://media.marionnaud.fr/medias/xyz/prd-front-100257559-2000x2000.png"));
  assert.equal(perfume("https://media.marionnaud.fr/medias/xyz/prd-back-100257559-2000x2000.png"), null);
  assert.equal(perfume("https://media.marionnaud.fr/medias/xyz/prd-front-100257560-2000x2000.png"), null);
  const poster = setup({ src: "https://media.desenio.com/site_images/small.jpg?auto=compress,format&fit=max&w=1200&variant=1" });
  assert.ok(poster("https://media.desenio.com/site_images/small.jpg?variant=1"));
  assert.equal(poster("https://media.desenio.com/site_images/small.jpg?variant=2"), null);
  assert.equal(poster("https://media.desenio.com/site_images/large.jpg?variant=1"), null);
  const boseURL = "https://assets.bosecreative.com/transform/fa37274a-7a64-45d5-b382-fb7f00334448/SkyPink_EcomGal1?format=avif&quality=95&io=width:816,height:667,transform:fit";
  const bose = setup({ src: `${boseURL}&io=width:816,height:667,transform:fit` });
  assert.ok(bose(boseURL));
  assert.equal(bose(boseURL.replace("fa37274a-7a64-45d5-b382-fb7f00334448", "3cff7eee-ac43-4346-84c2-ee8d49fcd9cd")), null);
  assert.equal(bose(boseURL.replace("SkyPink", "Black")), null);
  assert.equal(bose(boseURL.replace("quality=95", "quality=90")), null);
  assert.equal(bose(`${boseURL}&io=width:300,height:300,transform:fit`), null);
});

test("bounds and validates a PNG screenshot before decoding", () => {
  const context = { atob }; vm.runInNewContext(limitsSource, context);
  const png = (width, height) => { const header = Buffer.alloc(24); Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]).copy(header); header.write("IHDR", 12); header.writeUInt32BE(width, 16); header.writeUInt32BE(height, 20); return `data:image/png;base64,${header.toString("base64")}`; };
  assert.deepEqual(JSON.parse(JSON.stringify(context.monKadoScreenshotDimensions(png(1920, 1080)))), { width: 1920, height: 1080 });
  assert.equal(context.monKadoScreenshotDimensions(png(10000, 10000)), null);
  assert.equal(context.monKadoScreenshotDimensions(png(0, 1)), null);
  assert.equal(context.monKadoScreenshotDimensions("data:image/png;base64,not-a-png"), null);
  assert.equal(context.monKadoScreenshotDimensions("data:image/png;base64," + "A".repeat(32 * 1024 * 1024)), null);
});

test("repositions a matching image below a fixed overlay and never scrolls an oversized image", () => {
  let top = 100, calls = 0, height = 400;
  const img = { naturalWidth: 500, naturalHeight: 500, currentSrc: "https://cdn.example/product.png", getBoundingClientRect: () => ({ left: 100, top, right: 500, bottom: top + height, width: 400, height }) };
  const document = { baseURI: "https://shop.example/", images: [img], elementFromPoint: (_x, y) => y < 180 ? {} : img };
  const window = { innerWidth: 1280, innerHeight: 720, scrollBy: ({ top: delta }) => { calls++; top -= delta; } };
  const context = { document, window, URL, getComputedStyle: () => ({ visibility: "visible", opacity: "1" }) };
  vm.runInNewContext(source, context);
  assert.equal(context.monKadoVisibleImage(img.currentSrc), null);
  assert.equal(context.monKadoPositionImage(img.currentSrc).y, 216);
  assert.equal(calls, 1);
  height = 800; calls = 0;
  assert.equal(context.monKadoPositionImage(img.currentSrc), null); assert.equal(calls, 0);
});

test("accepts only the transparent Tediber zoom control over the selected image", () => {
  const img = { naturalWidth: 500, naturalHeight: 500, currentSrc: "https://www.tediber.com/cdn/shop/files/pillow.jpg?v=1&width=800", getBoundingClientRect: () => ({ left: 10, top: 20, right: 210, bottom: 220, width: 200, height: 200 }) };
  const hiddenLabel = { getBoundingClientRect: () => ({ width: 1, height: 1 }) };
  const overlay = { matches: () => true, parentElement: { contains: (element) => element === img }, childNodes: [], querySelectorAll: () => [hiddenLabel] };
  const document = { baseURI: "https://www.tediber.com/", images: [img], elementFromPoint: () => overlay, elementsFromPoint: () => [overlay, img] };
  const transparent = { visibility: "visible", opacity: "1", backgroundColor: "rgba(0, 0, 0, 0)", backgroundImage: "none", borderWidth: "0px", boxShadow: "none", filter: "none", backdropFilter: "none", mixBlendMode: "normal", content: '""' };
  let css = transparent;
  const context = { document, window: { innerWidth: 500, innerHeight: 500 }, URL, getComputedStyle: (element) => element === overlay ? css : transparent };
  vm.runInNewContext(source, context);
  const url = "https://www.tediber.com/cdn/shop/files/pillow.jpg?v=1";
  assert.ok(context.monKadoVisibleImage(url));
  css = { ...transparent, backgroundColor: "rgb(255, 255, 255)" };
  assert.equal(context.monKadoVisibleImage(url), null);
  css = { ...transparent, backdropFilter: "blur(5px)" };
  assert.equal(context.monKadoVisibleImage(url), null);
  css = transparent; hiddenLabel.getBoundingClientRect = () => ({ width: 20, height: 20 });
  assert.equal(context.monKadoVisibleImage(url), null);
  hiddenLabel.getBoundingClientRect = () => ({ width: 1, height: 1 }); document.baseURI = "https://other.example/";
  assert.equal(context.monKadoVisibleImage(url), null);
});
