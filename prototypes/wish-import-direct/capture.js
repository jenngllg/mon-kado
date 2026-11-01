// Runs in the merchant tab after the user explicitly requests a visible image copy.
globalThis.monKadoMatchingImages = function (imageURL) {
  let expected;
  try {
    const url = new URL(imageURL, document.baseURI);
    if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) return [];
    expected = url;
  } catch { return []; }
  const withoutWidth = (url) => JSON.stringify([...url.searchParams].filter(([key]) => key !== "width"));
  const sameResizedAsset = (actual) => {
    if (actual.origin !== expected.origin) return false;
    // Cloudflare's width-only path leaves the original asset path and query intact.
    const cloudflare = (url) => url.pathname.match(/^\/cdn-cgi\/image\/width=\d+(\/.*)$/)?.[1];
    const expectedAsset = cloudflare(expected), actualAsset = cloudflare(actual);
    if (expectedAsset && expectedAsset === actualAsset && actual.search === expected.search) return true;
    // Shopify serves responsive widths of the same file using a width query parameter.
    if (expected.pathname.startsWith("/cdn/shop/files/") && expected.pathname === actual.pathname && withoutWidth(expected) === withoutWidth(actual)) return true;
    // Desenio transforms the same named asset with image-format and size parameters.
    const desenioQuery = (url) => JSON.stringify([...url.searchParams].filter(([key]) => !["auto", "fit", "w", "h", "q"].includes(key)));
    if (expected.hostname === "media.desenio.com" && expected.pathname.startsWith("/site_images/") && expected.pathname === actual.pathname && desenioQuery(expected) === desenioQuery(actual)) return true;
    // Bose can repeat the same image transform parameter in the displayed URL.
    const boseQuery = (url) => {
      const seen = new Set();
      return JSON.stringify([...url.searchParams].filter(([key, value]) => {
        if (key !== "io") return true;
        if (seen.has(value)) return false;
        seen.add(value); return true;
      }));
    };
    if (expected.hostname === "assets.bosecreative.com" && expected.pathname.startsWith("/transform/") && expected.pathname === actual.pathname && boseQuery(expected) === boseQuery(actual)) return true;
    // Marionnaud publishes each view and variant at multiple named resolutions.
    const marionnaud = (url) => url.pathname.split("/").pop().match(/^prd-(front|back|side|last)-(\d{6,15})-\d+x\d+\.(png|jpe?g|webp)$/i)?.slice(1).map((value) => value.toLowerCase()).join("|");
    const expectedView = expected.hostname === "media.marionnaud.fr" && marionnaud(expected);
    if (expectedView && expectedView === marionnaud(actual) && expected.search === actual.search) return true;
    // Fnac uses the same filename for several gallery views; preserve the product and view number.
    if (expected.hostname === "static.fnac-static.com") {
      const fnac = (url) => url.pathname.match(/^(\/multimedia\/Images\/[^/]+\/MDM\/[^/]+\/[^/]+\/[^/]+\/\d+\/)\d+-(\d+)(\/tsp\d+\/[^/]+)$/i);
      const a = fnac(expected), b = fnac(actual);
      return !!a && !!b && a[1] === b[1] && a[2] === b[2] && a[3] === b[3] && expected.search === actual.search;
    }
    const filename = expected.pathname.split("/").pop();
    return /^[a-f0-9]{32,64}(?:_\d+)?\.(?:jpe?g|png|webp|avif)$/i.test(filename) && actual.search === expected.search && actual.pathname.split("/").pop() === filename;
  };
  const matches = [];
  for (const img of document.images) {
    if (!img.naturalWidth || !img.naturalHeight) continue;
    let src;
    try { src = new URL(img.currentSrc || img.src, document.baseURI); } catch { continue; }
    const exact = src.href === expected.href;
    const resized = sameResizedAsset(src);
    if (exact || resized) matches.push({ img, exact });
  }
  return matches;
};

globalThis.monKadoVisibleImage = function (imageURL) {
  const viewportWidth = window.innerWidth, viewportHeight = window.innerHeight;
  if (!viewportWidth || !viewportHeight) return null;
  const unobstructed = (img, x, y) => {
    const top = document.elementFromPoint(x, y);
    if (top === img) return true;
    // Tediber places a transparent zoom control over its product photo.
    const hostname = new URL(document.baseURI).hostname;
    const knownZoom = hostname === "www.tediber.com" && top?.matches?.("button.product__media-toggle.product__media-zoom-lightbox");
    if (!knownZoom ||
      !top.parentElement?.contains(img) || !document.elementsFromPoint(x, y).includes(img)) return false;
    const css = getComputedStyle(top);
    if (!["transparent", "rgba(0, 0, 0, 0)"].includes(css.backgroundColor) || css.backgroundImage !== "none" || css.boxShadow !== "none" || css.borderWidth !== "0px" ||
      css.filter !== "none" || css.backdropFilter !== "none" || css.mixBlendMode !== "normal") return false;
    if ([...top.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim())) return false;
    const noPaintedPseudo = (element) => ["::before", "::after"].every((pseudo) => {
      const style = getComputedStyle(element, pseudo);
      if (["none", "normal"].includes(style.content)) return true;
      return style.content === '""' && ["transparent", "rgba(0, 0, 0, 0)"].includes(style.backgroundColor) && style.backgroundImage === "none" && style.borderWidth === "0px" && style.boxShadow === "none" && style.filter === "none" && style.backdropFilter === "none" && style.mixBlendMode === "normal";
    });
    if ([...top.querySelectorAll("*")].some((child) => {
      const bounds = child.getBoundingClientRect(), style = getComputedStyle(child);
      return bounds.width > 2 || bounds.height > 2 || style.boxShadow !== "none" || style.borderWidth !== "0px" || style.filter !== "none" || style.backdropFilter !== "none" || !noPaintedPseudo(child);
    })) return false;
    return noPaintedPseudo(top);
  };
  const candidates = [];
  for (const { img, exact } of globalThis.monKadoMatchingImages(imageURL)) {
    const rect = img.getBoundingClientRect(), style = getComputedStyle(img);
    if (style.visibility !== "visible" || Number(style.opacity) === 0 || rect.width < 100 || rect.height < 100 ||
      rect.left < 0 || rect.top < 0 || rect.right > viewportWidth || rect.bottom > viewportHeight) continue;
    const points = [[0.1, 0.1], [0.9, 0.1], [0.5, 0.5], [0.1, 0.9], [0.9, 0.9]];
    if (points.some(([x, y]) => !unobstructed(img, rect.left + rect.width * x, rect.top + rect.height * y))) continue;
    candidates.push({ exact, rect: { x: rect.left, y: rect.top, width: rect.width, height: rect.height, viewportWidth, viewportHeight } });
  }
  candidates.sort((a, b) => Number(b.exact) - Number(a.exact) || b.rect.width * b.rect.height - a.rect.width * a.rect.height);
  return candidates[0]?.rect || null;
};

globalThis.monKadoPositionImage = function (imageURL) {
  const candidates = globalThis.monKadoMatchingImages(imageURL).map(({ img, exact }) => ({ img, exact, rect: img.getBoundingClientRect() }))
    .filter(({ img, rect }) => { const style = getComputedStyle(img); return style.visibility === "visible" && Number(style.opacity) !== 0 && rect.width >= 100 && rect.height >= 100 && rect.width <= window.innerWidth && rect.height <= window.innerHeight; });
  candidates.sort((a, b) => Number(b.exact) - Number(a.exact) || b.rect.width * b.rect.height - a.rect.width * a.rect.height);
  const img = candidates[0]?.img;
  if (!img) return null;
  for (const fraction of [0.3, 0.35, 0.4, 0.2, 0.15, 0.1, 0.05]) {
    const rect = img.getBoundingClientRect();
    window.scrollBy({ top: rect.top - window.innerHeight * fraction, behavior: "instant" });
    const visible = globalThis.monKadoVisibleImage(imageURL);
    if (visible) return visible;
  }
  return null;
};
