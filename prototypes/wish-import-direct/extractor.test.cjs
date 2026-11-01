const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const { test } = require("node:test");
const vm = require("node:vm");

const source = readFileSync(join(__dirname, "extractor.js"), "utf8");
function extract({ products = [], metas = {}, images = [], scopes = [], fnacPrices = [], merchantPrices = [], appleChoices = [], title = "", heading = "", headingNode = null, status = 200, address = "https://shop.example/product?variant=1#section" } = {}) {
  const metaNodes = Object.entries(metas).map(([key, value]) => ({ getAttribute: (attribute) => attribute === "property" ? key : attribute === "content" ? value : "" }));
  const document = {
    baseURI: address, title, images,
    querySelectorAll: (selector) => selector.startsWith("meta") ? metaNodes : selector === "[itemscope][itemtype]" ? scopes : selector.startsWith('[id="buy_box"]') ? fnacPrices : selector === 'input[type="radio"][name="dimensionCharging"]:checked' ? appleChoices : selector === 'script[type="application/ld+json"]' ? products.map((value) => ({ textContent: JSON.stringify(value) })) : merchantPrices,
    querySelector: () => headingNode || ({ textContent: heading }),
  };
  const context = { document, location: { href: address }, performance: { getEntriesByType: () => [{ responseStatus: status }] }, URL };
  vm.runInNewContext(source, context);
  return JSON.parse(JSON.stringify(context.monKadoExtractPage()));
}

test("extracts a product, a single euro price and a loaded CDN image", () => {
  const result = extract({ products: [{ "@type": "Product", name: "Gift", image: "media/pim/item", offers: { price: "16.95", priceCurrency: "EUR" } }], images: [{ currentSrc: "https://cdn.example/media/pim/item", naturalWidth: 500, naturalHeight: 500 }] });
  assert.deepEqual(result, { name: "Gift", price: "16,95", url: "https://shop.example/product?variant=1", image: "https://cdn.example/media/pim/item" });
});

test("uses the matching CDN image address before that image has loaded", () => {
  const image = "media/pim/12661219_z7c9ndpd_1";
  const result = extract({ products: [{ "@type": "Product", name: "Buffy", image }],
    images: [{ src: "https://cdn.cultura.com/cdn-cgi/image/width=660/media/pim/12661219_z7c9ndpd_1", naturalWidth: 0, naturalHeight: 0 }] });
  assert.equal(result.image, "https://cdn.cultura.com/cdn-cgi/image/width=660/media/pim/12661219_z7c9ndpd_1");
  const other = extract({ products: [{ "@type": "Product", name: "Buffy", image }], images: [{ src: "https://cdn.example/media/pim/12661219_z7c9ndpd_12", naturalWidth: 0, naturalHeight: 0 }] });
  assert.equal(other.image, "https://shop.example/media/pim/12661219_z7c9ndpd_1");
});

test("bounds deeply nested image metadata without recursive overflow", () => {
  let image = "https://cdn.example/item.jpg";
  for (let depth = 0; depth < 1200; depth++) image = [image];
  const result = extract({ products: [{ "@type": "Product", name: "A product", image }] });
  assert.equal(result.name, "A product"); assert.equal(result.image, "");
});

test("matches the page offer ahead of a recommendation", () => {
  const result = extract({ products: [{ "@graph": [{ "@type": "Product", name: "Recommendation", offers: { url: "/other", price: "2", priceCurrency: "EUR" } }, { "@type": "Product", name: "Current", offers: { url: "/product", price: "5.00", priceCurrency: "EUR" } }] }] });
  assert.equal(result.name, "Current"); assert.equal(result.price, "5"); assert.equal(result.image, "");
});

test("does not suggest an ambiguous or non-euro price", () => {
  const base = { "@type": "Product", name: "Gift" };
  assert.equal(extract({ products: [{ ...base, offers: [{ price: "5", priceCurrency: "EUR" }, { price: "6", priceCurrency: "EUR" }] }] }).price, "");
  assert.equal(extract({ products: [{ ...base, offers: { price: "5", priceCurrency: "USD" } }] }).price, "");
  assert.equal(extract({ products: [{ ...base, offers: [{ price: "5", priceCurrency: "EUR" }, { price: "7", priceCurrency: "USD" }] }] }).price, "");
});

test("uses only the selected Apple charging option when its visible price matches a structured offer", () => {
  const address = "https://www.apple.com/fr/shop/buy-airpods/airpods-5";
  const products = [{ "@type": "Product", name: "AirPods 5", offers: [{ price: 149, priceCurrency: "EUR" }, { price: 169, priceCurrency: "EUR" }] }];
  const choice = (price, { hidden = false, disabled = false, title = "AirPods 5" } = {}) => ({ disabled, labels: [{ querySelectorAll: (selector) => selector === ".form-selector-title" ? [{ childNodes: [{ nodeType: 3, textContent: title }, { nodeType: 1, textContent: "Description not part of the name" }] }] : [{ innerText: price, getClientRects: () => hidden ? [] : [{}], closest: () => null }] }] });
  for (const [selected, expected, name] of [["149,00 €", "149", "AirPods 5"], ["169,00 €", "169", "AirPods 5 avec Boîtier de charge sans fil"]]) {
    const result = extract({ address, products, appleChoices: [choice(selected, { title: name })] });
    assert.equal(result.price, expected); assert.equal(result.name, name); assert.equal(result.displayedVariantPrice, true);
  }
  for (const appleChoices of [[], [choice("199,00 €")], [choice("149,00 €", { hidden: true })], [choice("149,00 €", { disabled: true })], [choice("149,00 €", { title: "Different model" })], [choice("149,00 €", { title: `AirPods 5 ${"x".repeat(101)}` })], [choice("149,00 €"), choice("169,00 €")]]) {
    const result = extract({ address, products, appleChoices });
    assert.equal(result.price, ""); assert.equal(result.displayedVariantPrice, undefined);
  }
  const mixed = [{ ...products[0], offers: [...products[0].offers, { price: 200, priceCurrency: "USD" }] }];
  assert.equal(extract({ address, products: mixed, appleChoices: [choice("149,00 €")] }).price, "");
});

test("matches the Søstrene Grene query variant to its SKU and reports store-only availability", () => {
  const address = "https://sostrenegrene.com/fr/produits/vase?variant=1000093";
  const offer = (availability) => ({ url: "https://sostrenegrene.com/fr/produits/vase", price: "16.80", priceCurrency: "EUR", availability: `https://schema.org/${availability}` });
  const grey = { "@type": "Product", sku: "1000093", name: "Vase - Warm Grey", color: "Warm Grey", image: "https://cdn.example/grey.png", offers: offer("InStoreOnly") };
  const green = { "@type": "Product", sku: "1000094", name: "Vase - Light Green", color: "Light Green", image: "https://cdn.example/green.png", offers: offer("InStock") };
  const metas = { "og:title": "Vase | Grès | Warm Grey", "og:image": "https://cdn.example/stale-green.png", "product:price:amount": "16.80", "product:price:currency": "EUR" };
  const result = extract({ address, products: [grey, green], heading: "Vase", metas });
  assert.equal(result.name, "Vase | Grès | Warm Grey"); assert.equal(result.price, "16,8");
  assert.equal(result.image, "https://cdn.example/grey.png"); assert.equal(result.storeOnly, true); assert.equal(result.displayedVariantPrice, true);
  const unknown = extract({ address: address.replace("1000093", "unknown"), products: [grey, green], heading: "Vase", metas });
  assert.equal(unknown.name, "Vase"); assert.equal(unknown.price, ""); assert.equal(unknown.image, "");
  const duplicates = extract({ address: `${address}&variant=1000094`, products: [grey, green], heading: "Vase", metas });
  assert.equal(duplicates.price, ""); assert.equal(duplicates.image, "");
  const unavailable = extract({ address, products: [{ ...grey, offers: offer("OutOfStock") }, green], heading: "Vase", metas });
  assert.equal(unavailable.price, ""); assert.equal(unavailable.storeOnly, undefined);
  const next = extract({ address: address.replace("1000093", "1000094"), products: [grey, green], heading: "Vase", metas });
  assert.equal(next.name, "Vase - Light Green"); assert.equal(next.image, "https://cdn.example/green.png"); assert.equal(next.storeOnly, undefined);
});

test("uses social metadata when product metadata is absent and rejects unsafe image URLs", () => {
  const result = extract({ metas: { "og:title": "A gift", "og:price:amount": "69,90", "og:price:currency": "EUR", "og:image": "javascript:alert(1)" } });
  assert.equal(result.name, "A gift"); assert.equal(result.price, "69,9"); assert.equal(result.image, "");
});

test("does not present an HTTP error page as a product", () => {
  const result = extract({ status: 403, title: "Access denied", heading: "Please verify" });
  assert.deepEqual(result, { name: "", price: "", url: "https://shop.example/product?variant=1", image: "", blocked: true });
});

test("offers an explicitly marked name from a blocked product URL without inventing a price or image", () => {
  const result = extract({ status: 403, address: "https://www.fnac.com/Figurine-Funko-Pop-TV-Wednesday-Addams/a21262654/w-4" });
  assert.deepEqual(result, { name: "Figurine Funko Pop TV Wednesday Addams", price: "", url: "https://www.fnac.com/Figurine-Funko-Pop-TV-Wednesday-Addams/a21262654/w-4", image: "", blocked: true, nameFromURL: true });
});

test("marks a name derived from the URL when a successful page contains no product data", () => {
  const result = extract({ address: "https://shop.example/products/photo-album-portrait" });
  assert.deepEqual(result, { name: "photo album portrait", price: "", url: "https://shop.example/products/photo-album-portrait", image: "", nameFromURL: true });
});

test("offers a readable model or product and variant from blocked links without inventing fields", () => {
  const cases = [
    ["https://www.darty.com/nav/achat/petit_electromenager/petit-dejeuner/bouilloire_theiere_chocolatiere/smeg_wkf01cr.html", "smeg wkf01cr"],
    ["https://www.sezane.com/fr-fr/product/gilet-gaspard/beige-clair", "gilet gaspard beige clair"],
    ["https://www.bhv.fr/p/vase%2Bmarocain%2B-%2Borange%2Brose%2B-%2Bs-hay/300408518849/356", "vase marocain orange rose s hay"],
  ];
  for (const [address, expected] of cases) {
    const result = extract({ status: 403, address });
    assert.equal(result.name, expected); assert.equal(result.nameFromURL, true); assert.equal(result.price, ""); assert.equal(result.image, "");
  }
});

test("does not treat a UUID in a blocked URL as a name", () => {
  const result = extract({ status: 403, address: "https://shop.example/019c52dd-56c1-7cc6-8a95-243f3a032e04" });
  assert.equal(result.name, ""); assert.equal(result.blocked, true);
});

test("does not suggest a generic product identifier as a name", () => {
  const result = extract({ status: 403, address: "https://www.laredoute.fr/ppdp/prod-602771949.aspx" });
  assert.equal(result.name, ""); assert.equal(result.nameFromURL, undefined);
});

test("uses product microdata with an unambiguous euro offer", () => {
  const node = (text) => ({ getAttribute: (attribute) => attribute === "content" ? text : "", textContent: text });
  const offer = { querySelector: (selector) => selector.includes('"priceCurrency"') ? node("EUR") : node("13.90") };
  const scope = { getAttribute: () => "https://schema.org/Product", querySelector: (selector) => selector.includes('"name"') ? node("Microdata gift") : selector.includes('"image"') ? node("https://cdn.example/gift.jpg") : null, querySelectorAll: () => [offer] };
  const result = extract({ scopes: [scope] });
  assert.equal(result.name, "Microdata gift"); assert.equal(result.price, "13,9"); assert.equal(result.image, "https://cdn.example/gift.jpg");
});

test("ignores offers belonging to nested recommended products", () => {
  const node = (value) => ({ getAttribute: (attribute) => attribute === "content" ? value : "", textContent: value });
  const scope = { getAttribute: () => "https://schema.org/Product", querySelector: (selector) => selector.includes('"name"') ? node("Primary gift") : null };
  const makeOffer = (amount, owner) => ({ closest: () => owner, querySelector: (selector) => selector.includes('"priceCurrency"') ? node("EUR") : node(amount) });
  scope.querySelectorAll = () => [makeOffer("38.99", scope), makeOffer("19.99", {})];
  assert.equal(extract({ scopes: [scope], heading: "Primary gift" }).price, "38,99");
});

test("falls back to a loaded image matching the product heading", () => {
  const result = extract({ heading: "A book", images: [{ alt: "A book", currentSrc: "https://cdn.example/book.jpg", naturalWidth: 200, naturalHeight: 300 }] });
  assert.equal(result.name, "A book"); assert.equal(result.image, "https://cdn.example/book.jpg");
});

test("does not select a random product from conflicting recommendations", () => {
  const products = [{ "@type": "Product", name: "Other one", offers: { price: "2", priceCurrency: "EUR" } }, { "@type": "Product", name: "Other two", offers: { price: "3", priceCurrency: "EUR" } }];
  const result = extract({ products, heading: "Current product" });
  assert.equal(result.name, "Current product"); assert.equal(result.price, "");
});

test("identifies a challenge served as HTTP 200", () => {
  const result = extract({ title: "Pardon Our Interruption", address: "https://shop.example/lego-flower-set" });
  assert.equal(result.blocked, true); assert.equal(result.name, "lego flower set"); assert.equal(result.nameFromURL, true);
});

test("does not retain a redirected verification URL or its token", () => {
  for (const address of ["https://fr.loccitane.com/DDUser-Challenge?token=secret", "https://geo.captcha-delivery.com/captcha/?token=secret"]) {
    const result = extract({ address, status: 200 });
    assert.deepEqual(result, { name: "", price: "", url: "", image: "", blocked: true, verificationRedirect: true });
    assert.doesNotMatch(JSON.stringify(result), /secret/);
  }
});

test("does not mistake a browser error message for a product name", () => {
  const result = extract({ title: "Ce site est inaccessible", address: "https://shop.example/lego-flower-set" });
  assert.equal(result.blocked, true); assert.equal(result.name, "lego flower set"); assert.equal(result.nameFromURL, true);
});

test("offers a clearly marked shortened name when every title exceeds the form limit", () => {
  const result = extract({ heading: "Long product name ".repeat(8) });
  assert.ok(result.name.length <= 100); assert.equal(result.nameTruncated, true);
});

test("prefers the product title to a variant-only structured name", () => {
  const result = extract({ products: [{ "@type": "Product", name: "30ml - Rechargeable" }], metas: { "og:title": "Eau de parfum rechargeable" } });
  assert.equal(result.name, "Eau de parfum rechargeable");
});

test("does not suggest the name of the shop as a product", () => {
  const result = extract({ title: "Shop", address: "https://shop.example/product" });
  assert.equal(result.name, "");
});

test("chooses a matching seller offer rather than another product variant", () => {
  const offers = { "@type": "AggregateOffer", priceCurrency: "EUR", offers: [{ price: "21.99", priceCurrency: "EUR", url: "/product?variant=other" }, { price: "35.72", priceCurrency: "EUR", url: "/product?variant=1" }] };
  const result = extract({ products: [{ "@type": "Product", name: "Gift", offers }] });
  assert.equal(result.price, "35,72");
  assert.equal(extract({ products: [{ "@type": "Product", name: "Gift", offers }], address: "https://shop.example/product" }).price, "");
});

test("selects the structured product whose variant URL exactly matches the requested link", () => {
  const variants = [{ "@type": "Product", name: "Perfume 150ml", image: "https://cdn.example/large.jpg", offers: { url: "/product?variant=150", price: "209", priceCurrency: "EUR" } }, { "@type": "Product", name: "Perfume 30ml", image: "https://cdn.example/small.jpg", offers: { url: "/product?variant=1", price: "71.50", priceCurrency: "EUR" } }];
  const metas = { "og:image": "https://cdn.example/large.jpg" };
  const result = extract({ products: variants, heading: "Perfume", metas });
  assert.equal(result.name, "Perfume 30ml"); assert.equal(result.price, "71,5"); assert.equal(result.image, "https://cdn.example/small.jpg");
  const unknown = extract({ products: variants, heading: "Perfume", metas, address: "https://shop.example/product?variant=unknown" });
  assert.equal(unknown.name, "Perfume"); assert.equal(unknown.price, ""); assert.equal(unknown.image, "");
  const missing = extract({ products: [variants[0], { ...variants[1], image: undefined }], heading: "Perfume", metas });
  assert.equal(missing.image, "");
});

test("uses an unambiguous image shared by every size variant but never a different social image", () => {
  const address = "https://shop.example/product";
  const variants = ["36", "37"].map((size) => ({ "@type": "Product", name: `Sneakers ${size}`, image: "https://cdn.example/white.jpg", offers: { url: address, priceCurrency: "EUR", price: 80 } }));
  const metas = { "og:image": "https://cdn.example/black.jpg" };
  assert.equal(extract({ address, products: variants, heading: "Sneakers", metas }).image, "https://cdn.example/white.jpg");
  assert.equal(extract({ address, products: [variants[0], { ...variants[1], image: "https://cdn.example/black.jpg" }], heading: "Sneakers", metas }).image, "");
  assert.equal(extract({ address, products: [variants[0], { ...variants[1], image: undefined }], heading: "Sneakers", metas }).image, "");
});

test("deduplicates equivalent product metadata without trusting a conflicting cents-based social price", () => {
  const products = [{ "@type": "Product", name: "Gift", url: "/product", offers: { price: "32.8", priceCurrency: "EUR" } }, { "@type": "Product", name: "Gift", url: "/product", offers: { price: "32.80", priceCurrency: "EUR" } }];
  const result = extract({ products, metas: { "og:price:amount": "3280", "og:price:currency": "EUR" }, address: "https://shop.example/product" });
  assert.equal(result.price, "32,8");
  products[1].offers.price = "50";
  assert.equal(extract({ products, metas: { "og:price:amount": "3280", "og:price:currency": "EUR" }, address: "https://shop.example/product" }).price, "");
});

test("matches equivalent singleton offer arrays and objects without merging different offers", () => {
  const url = "https://shop.example/products/item";
  const one = { "@type": "Product", name: "Identical product", url, offers: { price: 69.95, priceCurrency: "EUR" } };
  const other = { "@type": "Product", name: "Identical product", url, offers: [{ price: "69.95", priceCurrency: "EUR" }] };
  assert.equal(extract({ address: url, heading: "Identical product", products: [one, other] }).price, "69,95");
  other.offers[0].price = "79.95";
  assert.equal(extract({ address: url, heading: "Identical product", products: [one, other] }).price, "");
  other.offers = [{ price: "69.95", priceCurrency: "EUR" }, { price: "79.95", priceCurrency: "EUR" }];
  assert.equal(extract({ address: url, heading: "Identical product", products: [one, other] }).price, "");
});

test("finds one nearby visible euro price but refuses ambiguous or shipping prices", () => {
  const element = (text, excluded = false) => ({ innerText: text, closest: () => excluded ? {} : null, getClientRects: () => [1] });
  const withPrices = (prices) => ({ textContent: "A product", parentElement: { querySelectorAll: () => prices, parentElement: null }, querySelectorAll: () => [] });
  assert.equal(extract({ headingNode: withPrices([element("36,99 €"), element("4,90 €", true)]) }).price, "36,99");
  assert.equal(extract({ headingNode: withPrices([element("36,99 €"), element("39,99 €")]) }).price, "");
});

test("finds a product image by matching its alternative text even before it loads", () => {
  const result = extract({ heading: "A useful product name", images: [{ alt: "A useful product name | Product image", src: "https://cdn.example/product.jpg", naturalWidth: 0, naturalHeight: 0 }] });
  assert.equal(result.image, "https://cdn.example/product.jpg");
});

test("reads the visible selected Fnac buy-box price and product image", () => {
  const heading = "Figurine Funko Pop TV Wednesday Addams";
  const price = (text, visible) => ({ innerText: text, getClientRects: () => visible ? [1] : [] });
  const result = extract({ address: "https://www.fnac.com/Figurine-Funko-Pop-TV-Wednesday-Addams/a21262654/w-4", heading,
    fnacPrices: [price("12,34 €", false), price("12,34 €", true)], images: [{ alt: heading, src: "https://static.fnac-static.com/product.jpg", naturalWidth: 340, naturalHeight: 340 }] });
  assert.equal(result.name, heading); assert.equal(result.price, "12,34"); assert.equal(result.image, "https://static.fnac-static.com/product.jpg");
  assert.equal(extract({ address: "https://www.fnac.com/product", heading, fnacPrices: [price("12,34 €", true), price("16,99 €", true)] }).price, "");
});

test("reads the Fnac Belgium product data when the verified page has a tracking query", () => {
  const name = "Figurine Funko Pop TV Wednesday Rave'n W";
  const url = "https://www.fr.fnac.be/POP-TV-Wednesday-Rave-n-W/a19427058";
  const result = extract({ address: `${url}?oref=example`, heading: name, products: [{ "@type": "Product", name, url,
    image: ["https://static.fnac-static.com/product.jpg"], offers: { price: 24.48, priceCurrency: "EUR" } }] });
  assert.equal(result.name, name); assert.equal(result.price, "24,48"); assert.equal(result.image, "https://static.fnac-static.com/product.jpg");
});

test("selects the ordinary Fnac price when a member price is also visible", () => {
  const price = (text, kind) => ({ innerText: text, className: `Pricing__pricingLabelMain Pricing__${kind}`, getClientRects: () => [1] });
  const result = extract({ address: "https://www.fnac.com/a1082197/Le-Petit-Prince", heading: "Le Petit Prince : Le Petit Prince", fnacPrices: [price("25 €", "default"), price("23,75 €", "green")] });
  assert.equal(result.price, "25");
  assert.equal(extract({ address: "https://www.fnac.com/product", fnacPrices: [price("25 €", "default"), price("23,75 €", "unknown")] }).price, "");
});

test("reads the visible current product price for supported merchant layouts and rejects conflicts", () => {
  const price = (text, visible = true) => ({ innerText: text, getClientRects: () => visible ? [1] : [] });
  for (const [address, amount] of [["https://www.cultura.com/p-item.html", "36,99"], ["https://www.nike.com/fr/t/item", "119,99"], ["https://www.bose.fr/p/item", "349,95"]]) {
    assert.equal(extract({ address, merchantPrices: [price(`${amount} €`), price("99 €", false)] }).price, amount);
    assert.equal(extract({ address, merchantPrices: [price(`${amount} €`), price("99 €")] }).price, "");
  }
  assert.equal(extract({ address: "https://other.example/item", merchantPrices: [price("36,99 €")] }).price, "");
});

test("matches a book edition and avoids an out-of-stock offer and another edition's social image", () => {
  const address = "https://books.example/livres/edition-123.html";
  const book = { "@type": "Book", name: "The book", image: "https://cdn.example/edition-123.jpg", offers: { url: address, price: 14.5, priceCurrency: "EUR", availability: "https://schema.org/OutOfStock" } };
  const result = extract({ address, products: [book], metas: { "og:image": "https://cdn.example/other-edition.jpg" } });
  assert.equal(result.name, "The book"); assert.equal(result.image, "https://cdn.example/edition-123.jpg"); assert.equal(result.price, "");
  assert.equal(extract({ address: "https://books.example/another", products: [book] }).image, "");
  book.offers.availability = "https://schema.org/InStock";
  assert.equal(extract({ address, products: [book] }).price, "14,5");
});

test("ignores concatenated social image URLs and falls back to the product image", () => {
  const result = extract({ address: "https://shop.example/product", metas: { "og:image": "https://cdn.examplehttps://images.example/product.jpg" },
    products: [{ "@type": "Product", name: "A product", image: "https://images.example/product.jpg" }] });
  assert.equal(result.image, "https://images.example/product.jpg");
});

test("matches a unique SKU path segment and deduplicates identical variants without guessing an offer", () => {
  const variant = { "@type": "Product", sku: "10389507-5", name: "Doudou rose", image: "/rose.jpg" };
  const other = { "@type": "Product", sku: "10389507-6", name: "Doudou bleu", image: "/bleu.jpg" };
  const address = "https://shop.example/p/10389507-5/doudou-lapin-rose";
  const result = extract({ address, products: [variant, variant, other], metas: { "og:image": "https://shop.example/bleu.jpg" } });
  assert.equal(result.name, "Doudou rose"); assert.equal(result.image, "https://shop.example/rose.jpg"); assert.equal(result.price, "");
  const conflict = { ...variant, image: "/another.jpg" };
  assert.equal(extract({ address, products: [variant, conflict, other] }).image, "");
  assert.equal(extract({ address: "https://shop.example/p/10389507-55/doudou", products: [variant, other] }).image, "");
});

test("keeps the visible edition when it extends the product title", () => {
  const result = extract({ heading: "Hunger Games Tome 1 (édition collector)", products: [{ "@type": "Product", name: "Hunger Games Tome 1" }] });
  assert.equal(result.name, "Hunger Games Tome 1 (édition collector)");
  assert.equal(extract({ heading: "Hunger Games Tome 10", products: [{ "@type": "Product", name: "Hunger Games Tome 1" }] }).name, "Hunger Games Tome 1");
});

test("uses one visible selected variant price and refuses conflicting visible selections", () => {
  const price = (text, visible = true) => ({ innerText: text, getClientRects: () => visible ? [1] : [] });
  for (const [address, text, expected] of [["https://www.spartoo.com/shoes", "110,00 €", "110"], ["https://www.tediber.com/products/pillow", "85,00 €", "85"], ["https://desenio.fr/p/poster", "Price: 31,95 €", "31,95"]]) {
    const product = { "@type": "Product", name: "Example product", offers: [{ price: "99", priceCurrency: "EUR" }, { price: "199", priceCurrency: "EUR" }] };
    const result = extract({ address, products: [product], merchantPrices: [price(text), price("200 €", false)] });
    assert.equal(result.price, expected); assert.equal(result.displayedVariantPrice, true);
    const hidden = { ...price("200 €"), closest: () => ({}) };
    assert.equal(extract({ address, products: [product], merchantPrices: [price(text), hidden] }).price, expected);
    assert.equal(extract({ address, products: [product], merchantPrices: [price(text), price("200 €")] }).price, "");
    assert.equal(extract({ address, products: [product], merchantPrices: [price("0,00 €")] }).price, "");
    const stale = { "@type": "Product", name: "Example product", offers: { price: "99", priceCurrency: "EUR" } };
    assert.equal(extract({ address, products: [stale], merchantPrices: [price(text), price("200 €")] }).price, "");
    assert.equal(extract({ address, products: [stale], merchantPrices: [price("0,00 €")] }).price, "");
  }
});

test("matches Desenio's selected format to its in-stock structured offer", () => {
  const title = "Botanic Letter P Personal Affiche";
  const offer = (price) => ({ availability: "https://schema.org/InStock", priceSpecification: [{ price, priceCurrency: "EUR" }] });
  const group = { "@type": "ProductGroup", name: title, hasVariant: [
    { "@type": "Product", name: `${title} 30x40 cm`, image: "https://media.desenio.com/site_images/small.jpg", offers: offer(31.95) },
    { "@type": "Product", name: `${title} 50x70 cm`, image: "https://media.desenio.com/site_images/large.jpg", offers: offer(41.95) },
  ] };
  const headingNode = (visible, selected) => ({ textContent: title, closest: () => ({ querySelectorAll: (selector) =>
    selector.startsWith("button") ? visible.map((textContent) => ({ textContent, getClientRects: () => [1] })) : selected.map((textContent) => ({ textContent })) }) });
  const address = "https://desenio.fr/p/poster";
  const result = extract({ address, products: [group], headingNode: headingNode(["30x40 cm"], ["30x40 cm"]) });
  assert.equal(result.price, "31,95"); assert.equal(result.displayedVariantPrice, true);
  const changed = extract({ address, products: [group], metas: { "og:image": "https://media.desenio.com/site_images/small.jpg?w=1200" }, headingNode: headingNode(["50x70 cm"], ["50x70 cm"]) });
  assert.equal(changed.price, "41,95"); assert.equal(changed.image, "https://media.desenio.com/site_images/large.jpg");
  const same = extract({ address, products: [group], metas: { "og:image": "https://media.desenio.com/site_images/small.jpg?w=1200" }, headingNode: headingNode(["30x40 cm"], ["30x40 cm"]) });
  assert.equal(same.image, "https://media.desenio.com/site_images/small.jpg?w=1200");
  group.hasVariant[1].image = "https://media.desenio.com/site_images/large.jpg?variant=2";
  const wrongQuery = extract({ address, products: [group], metas: { "og:image": "https://media.desenio.com/site_images/large.jpg?variant=1&w=1200" }, headingNode: headingNode(["50x70 cm"], ["50x70 cm"]) });
  assert.equal(wrongQuery.image, "https://media.desenio.com/site_images/large.jpg?variant=2");
  group.hasVariant[1].image = "";
  const missing = extract({ address, products: [group], metas: { "og:image": "https://media.desenio.com/site_images/small.jpg" }, headingNode: headingNode(["50x70 cm"], ["50x70 cm"]) });
  assert.equal(missing.image, "");
  assert.equal(extract({ address, products: [group], headingNode: headingNode(["30x40 cm"], ["50x70 cm"]) }).price, "");
  assert.equal(extract({ address, products: [group], headingNode: headingNode(["30x40 cm", "50x70 cm"], ["30x40 cm"]) }).price, "");
  const tooMany = { ...group, hasVariant: [...group.hasVariant, ...Array.from({ length: 99 }, (_, i) => ({ "@type": "Product", name: `Other ${i}` }))] };
  assert.equal(extract({ address, products: [tooMany], headingNode: headingNode(["30x40 cm"], ["30x40 cm"]) }).price, "");
  group.hasVariant[0].offers.availability = "https://schema.org/OutOfStock";
  assert.equal(extract({ address, products: [group], headingNode: headingNode(["30x40 cm"], ["30x40 cm"]) }).price, "");
});
