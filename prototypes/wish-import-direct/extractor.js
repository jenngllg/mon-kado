// Runs in the browser's isolated extension world after the user opens a page.
globalThis.monKadoExtractPage = function () {
  const compact = (value) => typeof value === "string" ? value.replace(/\s+/gu, " ").trim() : "";
  const validName = (value) => { const s = compact(value); return s.length > 0 && [...s].length <= 100 ? s : ""; };
  const webURL = (value) => {
    if (typeof value !== "string" || !value.trim()) return "";
    // Some pages concatenate two absolute URLs into an invalid social image address.
    if (/^https?:\/\/[^/?#]*https?:\/\//i.test(value.trim())) return "";
    try {
      const u = new URL(value, document.baseURI);
      return (u.protocol === "http:" || u.protocol === "https:") && !u.username && !u.password ? u.href : "";
    } catch { return ""; }
  };
  const currentURL = new URL(location.href); currentURL.hash = "";
  // Verification URLs may contain short-lived provider tokens and are not product links.
  if (/(?:^|\.)captcha-delivery\.com$/i.test(currentURL.hostname) || /(?:^|\/)DDUser[-_]Challenge(?:\/|$)/i.test(currentURL.pathname)) {
    return { name: "", price: "", url: "", image: "", blocked: true, verificationRedirect: true };
  }
  const nameFromAddress = () => {
    const slugs = currentURL.pathname.split("/").map((segment) => {
      try { return decodeURIComponent(segment).replace(/\.(?:html?|aspx?|php)$/i, ""); } catch { return ""; }
    }).filter((segment) => segment.length >= 8 && segment.length <= 120 && /[-_+]/u.test(segment) &&
      (segment.match(/\p{L}/gu) || []).length >= 4 && !/[\u0000-\u001f\u007f/\\?#@]/u.test(segment) && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(segment) && !/^(?:prod(?:uct)?|productpage|item|ref|sku)[-_+]*\d+$/i.test(segment));
    const last = slugs.at(-1) || "", previous = slugs.at(-2) || "";
    const joinVariant = previous && (last.match(/[-_+]/g) || []).length === 1 && (previous.match(/[-_+]/g) || []).length === 1;
    const readable = (value) => value.replace(/[-_+]+/g, " ");
    return joinVariant && validName(readable(`${previous}-${last}`)) || validName(readable(last));
  };
  const navigation = performance.getEntriesByType("navigation")[0];
  const firstHeading = compact(document.querySelector("h1")?.textContent);
  const challenge = /access denied|accès refusé|verify you are human|vérifi(?:ez|cation).*humain|captcha|just a moment|pardon our interruption|robot check|ce site est inaccessible|this site can.t be reached|webpage not available|page (?:introuvable|non trouvée)/i.test(`${document.title || ""} ${firstHeading}`);
  if (navigation && navigation.responseStatus >= 400 || challenge) {
    // The address is user supplied; a readable slug is only a tentative name, never a price or image.
    const name = nameFromAddress();
    return { name, price: "", url: currentURL.href, image: "", blocked: true, ...(name ? { nameFromURL: true } : {}) };
  }
  const meta = Object.create(null);
  for (const node of document.querySelectorAll("meta[property], meta[name]")) {
    const key = (node.getAttribute("property") || node.getAttribute("name") || "").toLowerCase();
    if (!(key in meta)) meta[key] = node.getAttribute("content") || "";
  }
  const products = []; const books = []; const groups = [];
  for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
    const text = script.textContent || "";
    if (text.length > 2 * 1024 * 1024) continue;
    let data;
    try { data = JSON.parse(text); } catch { continue; }
    const pending = [data]; let visited = 0;
    while (pending.length && visited++ < 10000) {
      const item = pending.pop();
      if (!item || typeof item !== "object") continue;
      if (Array.isArray(item)) { pending.push(...item.slice(0, 10000)); continue; }
      const types = Array.isArray(item["@type"]) ? item["@type"] : [item["@type"]];
      if (types.some((type) => typeof type === "string" && /(?:^|\/)product$/i.test(type)) && (item.name || item.image || item.offers)) products.push(item);
      if (types.some((type) => typeof type === "string" && /(?:^|\/)book$/i.test(type)) && item.offers) books.push(item);
      if (types.some((type) => typeof type === "string" && /(?:^|\/)productgroup$/i.test(type)) && Array.isArray(item.hasVariant)) groups.push(item);
      for (const value of Object.values(item)) if (value && typeof value === "object") pending.push(value);
    }
  }
  // An offer pointing at this page is a stronger signal than a recommendation.
  const matchesPage = (product, exactQuery = false) => {
    const candidates = [product.url, ...(Array.isArray(product.offers) ? product.offers : [product.offers]).map((o) => o?.url)];
    return candidates.some((candidate) => { try { const u = new URL(candidate, document.baseURI); return u.origin === currentURL.origin && u.pathname === currentURL.pathname && (!exactQuery || u.search === currentURL.search); } catch { return false; } });
  };
  // A Book's exact offer URL can identify its edition even when the page advertises other editions.
  if (!products.some((item) => matchesPage(item))) products.push(...books.filter((item) => matchesPage(item, !!currentURL.search)));
  const heading = firstHeading;
  const identity = [heading, compact(meta["og:title"])].filter(Boolean).map((value) => value.toLowerCase());
  const pathMatches = products.filter((item) => matchesPage(item));
  const exactMatches = currentURL.search ? pathMatches.filter((item) => matchesPage(item, true)) : [];
  const variantParameters = currentURL.searchParams.getAll("variant");
  // Søstrene Grene embeds a product per color while each offer points to the same canonical page.
  const queryVariantExpected = currentURL.hostname.toLowerCase() === "sostrenegrene.com" && variantParameters.length > 0;
  const queryVariantMatches = queryVariantExpected && variantParameters.length === 1 && variantParameters[0] ? pathMatches.filter((item) => typeof item.sku === "string" && item.sku === variantParameters[0]) : [];
  const pathSegments = currentURL.pathname.split("/").map((segment) => { try { return decodeURIComponent(segment); } catch { return ""; } });
  const skuMatches = products.filter((item) => typeof item.sku === "string" && item.sku && pathSegments.includes(item.sku));
  const matchingName = (items) => items.filter((item) => identity.includes(compact(item.name).toLowerCase()));
  const offerIdentity = (item) => {
    const offer = Array.isArray(item?.offers) && item.offers.length === 1 ? item.offers[0] : item?.offers;
    const amount = String(offer?.price ?? "").trim();
    if (!compact(item?.name) || !/^\d{1,8}(?:\.\d{1,2})?$/.test(amount) || !compact(offer?.priceCurrency)) return "";
    const [whole, fraction = ""] = amount.split(".");
    return `${compact(item.name).toLowerCase()}|${compact(offer.priceCurrency).toUpperCase()}|${whole.replace(/^0+(?=\d)/, "")}.${fraction.replace(/0+$/, "")}`;
  };
  const chooseUnique = (items) => {
    if (items.length === 1) return items[0];
    const named = matchingName(items);
    if (named.length === 1) return named[0];
    const signatures = items.map(offerIdentity);
    if (signatures.length && signatures[0] && signatures.every((value) => value === signatures[0])) return items[0];
    const noOfferKeys = items.map((item) => !item.offers && compact(item.sku) && compact(item.name) && typeof item.image === "string" ? `${compact(item.sku)}|${compact(item.name)}|${item.image}` : "");
    return noOfferKeys.length && noOfferKeys[0] && noOfferKeys.every((key) => key === noOfferKeys[0]) ? items[0] : null;
  };
  const candidatesForProduct = queryVariantExpected ? queryVariantMatches : exactMatches.length ? exactMatches : pathMatches.length ? pathMatches : skuMatches.length ? skuMatches : products;
  let product = chooseUnique(candidatesForProduct);
  const queryVariantSelected = queryVariantExpected && queryVariantMatches.length === 1 && product === queryVariantMatches[0];
  const ambiguousProducts = queryVariantExpected && !queryVariantSelected || candidatesForProduct.length > 1 && !product;
  if (!product && !queryVariantExpected) {
    // Microdata is another standard way to mark up the product already displayed on the page.
    const scopes = [...document.querySelectorAll("[itemscope][itemtype]")].filter((node) => /(?:^|\s)https?:\/\/schema\.org\/Product(?:\s|$)/i.test(node.getAttribute("itemtype") || "")).slice(0, 100);
    const value = (node) => node?.getAttribute("content") || node?.getAttribute("src") || node?.getAttribute("href") || node?.textContent || "";
    const scopeProduct = scopes.map((scope) => {
      const offerScopes = [...scope.querySelectorAll('[itemprop~="offers"]')].filter((offer) =>
        !offer.closest || offer.closest('[itemscope][itemtype*="schema.org/Product" i]') === scope).slice(0, 100);
      const sources = offerScopes.length ? offerScopes : [scope];
      return {
        name: value(scope.querySelector('[itemprop~="name"]')),
        image: value(scope.querySelector('[itemprop~="image"]')),
        url: value(scope.querySelector('[itemprop~="url"]')),
        offers: sources.map((offer) => ({ price: value(offer.querySelector('[itemprop~="price"]')), priceCurrency: value(offer.querySelector('[itemprop~="priceCurrency"]')) })),
      };
    });
    const scopeMatches = scopeProduct.filter((item) => matchesPage(item));
    const exactScopes = currentURL.search ? scopeMatches.filter((item) => matchesPage(item, true)) : [];
    product = exactScopes.length ? chooseUnique(exactScopes) : scopeMatches.length ? chooseUnique(scopeMatches) : chooseUnique(scopeProduct);
    product ||= {};
  }
  product ||= {};
  // A product selected among variants must not inherit a social image for another variant.
  const explicitVariantSelected = queryVariantSelected || (!queryVariantExpected && (
    (exactMatches.length > 0 && exactMatches.includes(product) && products.some((item) => !exactMatches.includes(item))) ||
    (!exactMatches.length && !pathMatches.length && skuMatches.length > 0 && skuMatches.includes(product) && products.some((item) => !skuMatches.includes(item)))
   ));
  const host = currentURL.hostname.replace(/^www\./i, "").toLowerCase();
  const variantName = /^\d+(?:[.,]\d+)?\s?(?:ml|cl|l|g|kg|oz|cm|mm)\b/i.test(compact(product.name));
  const productName = compact(product.name);
  const headingSuffix = heading.slice(productName.length);
  const specificHeading = productName.length >= 8 && heading.toLowerCase().startsWith(productName.toLowerCase()) && /^[\s(\[–—-]/u.test(headingSuffix) && validName(heading) ? heading : "";
  const candidates = [specificHeading, variantName ? "" : product.name, meta["og:title"], meta["twitter:title"], heading, document.title, product.name].map(compact)
    .filter((candidate) => candidate && ![host, host.split(".")[0]].includes(candidate.toLowerCase()));
  let name = candidates.map(validName).find(Boolean) || "";
  if (queryVariantExpected && !queryVariantSelected) name = validName(heading);
  if (queryVariantSelected && compact(product.color)) {
    const socialTitle = validName(meta["og:title"]);
    const parts = socialTitle.split("|").map(compact);
    if (parts.length > 1 && parts.at(-1).toLowerCase() === compact(product.color).toLowerCase() && parts[0].toLowerCase() === heading.toLowerCase()) name = socialTitle;
  }
  const nameTruncated = !name && candidates.length > 0;
  if (nameTruncated) name = [...candidates[0]].slice(0, 100).join("").trimEnd();
  const amounts = new Set(); let unsupported = false; let displayedVariantPrice = false; let selectedVariant = explicitVariantSelected ? product : null;
  const readOffer = (offer, inheritedCurrency) => {
    if (Array.isArray(offer)) { offer.slice(0, 100).forEach((entry) => readOffer(entry, inheritedCurrency)); return; }
    if (!offer || typeof offer !== "object") return;
    const currency = compact(offer.priceCurrency || inheritedCurrency).toUpperCase();
    if (offer.price !== undefined && offer.price !== null && String(offer.price).trim() !== "") {
      const amount = String(offer.price).trim().replace(",", ".");
      if (currency !== "EUR") unsupported = true;
      else if (/^\d{1,8}(?:\.\d{1,2})?$/.test(amount) && /[1-9]/.test(amount)) {
        const [whole, fraction = ""] = amount.split(".");
        amounts.add(`${whole.replace(/^0+(?=\d)/, "")}${fraction.replace(/0+$/, "") ? `.${fraction.replace(/0+$/, "")}` : ""}`);
      }
    } else if (offer.priceSpecification) readOffer(offer.priceSpecification, currency);
    else if (Array.isArray(offer.offers)) {
      const selected = currentURL.search && offer.offers.filter((item) => {
        try { const link = new URL(item.url, document.baseURI); return link.origin === currentURL.origin && link.pathname === currentURL.pathname && link.search === currentURL.search; } catch { return false; }
      });
      readOffer(selected?.length ? selected : offer.offers, currency);
    }
  };
  const unavailableBook = books.includes(product) && /(?:^|\/)outofstock$/i.test(product.offers?.availability || "");
  const unavailableVariant = queryVariantSelected && /(?:^|\/)outofstock$/i.test(product.offers?.availability || "");
  const storeOnly = queryVariantSelected && /(?:^|\/)instoreonly$/i.test(product.offers?.availability || "");
  if (!unavailableBook && !unavailableVariant) readOffer(product.offers, "");
  if (!amounts.size && !unsupported && !ambiguousProducts && !unavailableBook && !unavailableVariant) readOffer({ price: meta["product:price:amount"] || meta["og:price:amount"], priceCurrency: meta["product:price:currency"] || meta["og:price:currency"] }, "");
  if (!amounts.size && !unsupported && currentURL.hostname.toLowerCase() === "desenio.fr") {
    // The selected format ties a ProductGroup's offer to this product, not to another size.
    const section = document.querySelector("h1")?.closest?.("section");
    const labels = [...(section?.querySelectorAll('button[aria-haspopup="listbox"] span.truncate') || [])]
      .filter((node) => !node.getClientRects || node.getClientRects().length).map((node) => compact(node.textContent));
    const selected = [...(section?.querySelectorAll('[role="option"][aria-selected="true"] span.truncate') || [])].map((node) => compact(node.textContent));
    const matchingGroups = groups.filter((group) => compact(group.name).toLowerCase() === heading.toLowerCase());
    if (labels.length === 1 && selected.length === 1 && labels[0] && labels[0] === selected[0] && matchingGroups.length === 1 && matchingGroups[0].hasVariant.length <= 100) {
      const group = matchingGroups[0];
      const variants = group.hasVariant.filter((variant) => compact(variant?.name) === `${compact(group.name)} ${labels[0]}`);
      if (variants.length === 1 && /(?:^|\/)instock$/i.test(variants[0]?.offers?.availability || "")) {
        selectedVariant = variants[0];
        readOffer(variants[0].offers, "");
        displayedVariantPrice = amounts.size === 1 && !unsupported;
      }
    }
  }
  const visibleEuro = (element) => {
    const match = compact(element.innerText || "").match(/^(\d{1,8})(?:\s*[.,]\s*(\d{1,2}))?\s*€$/u);
    return match ? `${match[1]}${match[2] ? `.${match[2]}` : ""}` : "";
  };
  if (!unsupported && !ambiguousProducts && amounts.size > 1 && currentURL.hostname.toLowerCase() === "www.apple.com" && currentURL.pathname.startsWith("/fr/shop/buy-airpods/")) {
    // The AirPods buying page has multiple offers until the user selects a charging option.
    const selected = [...document.querySelectorAll('input[type="radio"][name="dimensionCharging"]:checked')];
    if (selected.length === 1 && !selected[0].disabled && selected[0].labels?.length === 1) {
      const label = selected[0].labels[0];
      const titles = [...label.querySelectorAll('.form-selector-title')];
      // Apple nests descriptive copy in the title; only its direct text names the model.
      const selectedName = titles.length === 1 ? validName([...titles[0].childNodes].filter((node) => node.nodeType === 3).map((node) => node.textContent).join(" ")) : "";
      const matchesProduct = productName && (selectedName.toLowerCase() === productName.toLowerCase() || selectedName.toLowerCase().startsWith(`${productName.toLowerCase()} `));
      const prices = [...label.querySelectorAll('.price-point-fullPrice-short')]
        .filter((element) => (!element.getClientRects || element.getClientRects().length) && !element.closest?.('[hidden], [aria-hidden="true"], del, s') && (typeof getComputedStyle !== "function" || getComputedStyle(element).visibility === "visible"));
      if (matchesProduct && prices.length === 1) {
        const amount = visibleEuro(prices[0]);
        const [whole, fraction = ""] = amount.split(".");
        const normalized = whole.replace(/^0+(?=\d)/, "") + (fraction.replace(/0+$/, "") ? `.${fraction.replace(/0+$/, "")}` : "");
        if (amount && amounts.has(normalized)) {
          name = selectedName; amounts.clear(); amounts.add(normalized); displayedVariantPrice = true;
        }
      }
    }
  }
  if (!unsupported) {
    // The displayed variant price may disambiguate several structured offers.
    const variantSelectors = {
      "www.spartoo.com": ".productPrice .display_price_by_size_id .product_price_span",
      "www.tediber.com": "product-buy-price .product-form__price-block__price",
      "desenio.fr": 'button[aria-haspopup="listbox"] .selected-option-price',
    };
    const selector = variantSelectors[currentURL.hostname.toLowerCase()];
    if (selector) {
      const displayed = new Set(); let invalidDisplayed = false;
      for (const element of document.querySelectorAll(selector)) {
        if (element.getClientRects && !element.getClientRects().length) continue;
        if (element.closest?.('[hidden], [aria-hidden="true"], del, s')) continue;
        if (typeof getComputedStyle === "function" && getComputedStyle(element).visibility !== "visible") continue;
        const text = compact(element.innerText || "").replace(/^Price:\s*/i, "");
        const match = text.match(/^(\d{1,8})(?:\s*[.,]\s*(\d{1,2}))?\s*€$/u);
        if (match && /[1-9]/.test(`${match[1]}${match[2] || ""}`)) {
          const whole = match[1].replace(/^0+(?=\d)/, ""), fraction = (match[2] || "").replace(/0+$/, "");
          displayed.add(`${whole}${fraction ? `.${fraction}` : ""}`);
        } else invalidDisplayed = true;
      }
      if (invalidDisplayed || displayed.size > 1) {
        amounts.clear(); unsupported = true;
      } else if (displayed.size === 1) {
        amounts.clear(); for (const amount of displayed) amounts.add(amount);
        displayedVariantPrice = true;
      }
    }
  }
  if (!amounts.size && !unsupported && !ambiguousProducts && /(?:^|\.)fnac\.(?:com|be)$/i.test(currentURL.hostname)) {
    // The visible buy box is the selected Fnac offer; other offers have different prices.
    const prices = [...document.querySelectorAll('[id="buy_box"] [class*="pricingLabelMain"]')]
      .filter((element) => !element.getClientRects || element.getClientRects().length);
    // Fnac can show a green member-only price beside the ordinary default price.
    const ordinary = prices.filter((element) => /(?:^|\s)\S*__default(?:\s|$)/u.test(element.className || ""));
    const selected = ordinary.length && prices.every((element) => /(?:^|\s)\S*__(?:default|green)(?:\s|$)/u.test(element.className || "")) ? ordinary : prices;
    for (const element of selected) {
      const amount = visibleEuro(element);
      if (amount) readOffer({ price: amount, priceCurrency: "EUR" }, "");
    }
  }
  if (!amounts.size && !unsupported) {
    // These selectors identify the current product price, not prices in recommendations.
    const selectors = {
      "www.cultura.com": ".one-pdp ~ * .price--big, .one-section .price--big",
      "www.nike.com": '#price-container [data-testid="currentPrice-container"]',
      "www.bose.fr": ".js-pdp-price .sales .value",
    };
    const selector = selectors[currentURL.hostname.toLowerCase()];
    if (selector) for (const element of document.querySelectorAll(selector)) {
      if (element.getClientRects && !element.getClientRects().length) continue;
      const amount = visibleEuro(element);
      if (amount) readOffer({ price: amount, priceCurrency: "EUR" }, "");
    }
  }
  const hasOfferEvidence = (offer) => Array.isArray(offer) ? offer.some(hasOfferEvidence) : !!offer && typeof offer === "object" &&
    (String(offer.price ?? "").trim() !== "" || String(offer.lowPrice ?? "").trim() !== "" || !!offer.priceSpecification || hasOfferEvidence(offer.offers));
  if (!amounts.size && !unsupported && !ambiguousProducts && !hasOfferEvidence(product.offers)) {
    // A nearby price with an explicit euro symbol is a suggestion only when unambiguous.
    let ancestor = document.querySelector("h1");
    for (let level = 0; ancestor && level < 8; level++, ancestor = ancestor.parentElement) {
      const values = new Set();
      for (const element of ancestor.querySelectorAll?.('[class*="price" i]') || []) {
        if (element.closest?.('[hidden], del, s, [class*="shipping" i], [class*="delivery" i], [class*="livraison" i], [class*="unit-price" i], [class*="old-price" i], [class*="price--old" i]')) continue;
        if (element.getClientRects && !element.getClientRects().length) continue;
        const amount = visibleEuro(element);
        if (amount) values.add(amount);
      }
      if (values.size) {
        for (const amount of values) readOffer({ price: amount, priceCurrency: "EUR" }, "");
        break;
      }
    }
  }
  const price = amounts.size === 1 && !unsupported ? [...amounts][0].replace(".", ",") : "";
  const imageValues = (value) => {
    const pending = [value], values = []; let visited = 0;
    while (pending.length && visited++ < 1000 && values.length < 100) {
      const item = pending.pop();
      if (typeof item === "string") values.push(item);
      else if (Array.isArray(item)) pending.push(...item.slice(0, 100).reverse());
      else if (item && typeof item === "object") pending.push(item.url || item.contentUrl);
    }
    return values;
  };
  const images = imageValues(product.image).filter((image) => image.trim());
  const selectedImages = imageValues(selectedVariant?.image).map(webURL).filter(Boolean);
  const socialImages = [meta["og:image:secure_url"], meta["og:image"], meta["twitter:image"]];
  const selectedSocial = socialImages.filter((image) => {
    const url = webURL(image); if (!url) return false;
    return selectedImages.some((candidate) => {
      const a = new URL(url), b = new URL(candidate);
      if (a.origin !== b.origin || a.pathname !== b.pathname || a.hash !== b.hash) return false;
      const relevant = (value) => JSON.stringify([...value.searchParams].filter(([key]) => a.hostname !== "media.desenio.com" || !["auto", "fit", "w", "h", "q"].includes(key)));
      return relevant(a) === relevant(b);
    });
  });
  const pageImages = Array.from({ length: Math.min(document.images.length, 500) }, (_, index) => document.images[index]);
  const loaded = pageImages.filter((img) => img.naturalWidth >= 150 && img.naturalHeight >= 150 && webURL(img.currentSrc || img.src));
  // Some sites put an asset key in JSON-LD but the full CDN URL on the loaded image.
  const matchingImage = images.map((image) => loaded.find((img) => (img.currentSrc || img.src).includes(image))).find(Boolean);
  // The page can already expose the CDN URL before the browser has loaded the image.
  const assetKeys = images.filter((image) => /^(?:\/?[a-z0-9_-]+\/)+[a-z0-9_.-]+$/i.test(image) && !image.split("/").includes(".."));
  const unloadedAssetURL = assetKeys.map((key) => {
    const suffix = `/${key.replace(/^\/+/, "")}`;
    for (const img of pageImages) for (const candidate of [img.currentSrc, img.getAttribute?.("data-src"), img.src]) {
      const url = webURL(candidate);
      if (url && new URL(url).pathname.endsWith(suffix)) return url;
    }
    return "";
  }).find(Boolean);
  const headingImage = heading && loaded.find((img) => compact(img.alt).toLowerCase() === heading.toLowerCase());
  const headingKey = heading.toLowerCase();
  const matchingAlt = heading && pageImages.find((img) => {
    const alt = compact(img.alt).toLowerCase();
    return alt === headingKey || headingKey.length >= 15 && alt.startsWith(`${headingKey} |`);
  });
  const imageCandidates = selectedVariant ? [...selectedSocial, ...selectedImages] : [...(books.includes(product) ? images : []), ...socialImages, matchingImage?.currentSrc, matchingImage?.src, unloadedAssetURL, ...images, headingImage?.currentSrc, headingImage?.src,
    matchingAlt?.currentSrc, matchingAlt?.getAttribute?.("data-src"), matchingAlt?.src];
  const ambiguousPageVariants = ambiguousProducts && (queryVariantExpected || pathMatches.length > 1);
  // Size variants sometimes all use one identical product image even without a selected size.
  const variantImages = ambiguousPageVariants && !queryVariantExpected && pathMatches.length <= 100 ? pathMatches.map((item) => imageValues(item.image).map(webURL).find(Boolean) || "") : [];
  const commonVariantImage = variantImages.length > 1 && variantImages[0] && variantImages.every((image) => image === variantImages[0]) ? variantImages[0] : "";
  const image = ambiguousPageVariants && !selectedVariant ? commonVariantImage : imageCandidates.map(webURL).find(Boolean) || "";
  if (!name && !price && !image) {
    const fallback = nameFromAddress();
    if (fallback) return { name: fallback, price: "", url: currentURL.href, image: "", nameFromURL: true };
  }
  return { name, price, url: currentURL.href, image, ...(nameTruncated ? { nameTruncated: true } : {}), ...(price && (displayedVariantPrice || queryVariantSelected) ? { displayedVariantPrice: true } : {}), ...(storeOnly ? { storeOnly: true } : {}) };
};
