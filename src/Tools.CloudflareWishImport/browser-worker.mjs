import { authenticated, bounded, merchantHost, validateDNS } from './worker.mjs';

const htmlMaximum = 4 * 1024 * 1024;
const networkMaximum = 8 * 1024 * 1024;
const browserTimeout = 6000;
const maximumRequests = 40;
const maximumHosts = 6;
const freeBrowserReserve = 500;
const imageMaximum = 2 * 1024 * 1024;
// Keep every network-capable execution context inside this intercepted page.
const browserCSP = "sandbox allow-scripts allow-same-origin; worker-src 'none'; child-src 'none'; frame-src 'none'; object-src 'none'; form-action 'none'";

const readyExpression = `new Promise(resolve => {
  const ready = () => [...document.querySelectorAll('script[type="application/ld+json"]')]
    .some(node => /"@type"\\s*:\\s*"(?:Product|ProductGroup|Book)"/.test(node.textContent || '')) ||
    document.querySelector('meta[property="product:price:amount"],meta[property="og:price:amount"]');
  if (ready()) return resolve(true);
  const interval = setInterval(() => { if (ready()) { clearInterval(interval); resolve(true); } }, 50);
  setTimeout(() => { clearInterval(interval); resolve(false); }, 1000);
})`;

function safeBrowserURL(address) {
  const target = new URL(address);
  if (target.protocol !== 'https:' || target.username || target.password || target.port || !merchantHost(target.hostname)) throw new Error('URL rejected');
  target.hash = '';
  return target;
}

/** Render anonymous public HTML under the same guarded network boundary as fetch. */
export async function handleRender(request, env, execution = { waitUntil() {} }, upstream = fetch) {
  if (!authenticated(request, env)) return new Response(null, { status: 401 });
  const route = new URL(request.url).pathname;
  if (request.method !== 'POST' || !['/render', '/limits'].includes(route)) return new Response(null, { status: 404 });
  if (request.headers.get('Content-Type') !== 'application/json') return new Response(null, { status: 415 });
  if (!env.BROWSER) return new Response(null, { status: 503 });
  const stop = new AbortController();
  const signal = AbortSignal.any([request.signal, stop.signal, AbortSignal.timeout(browserTimeout)]);
  let session, socket, timeout, originStatus, finalURL, mainFrame;
  let stage = 'limits', imageURL, imageRequest, imageMedia;
  const started = Date.now();
  let imageSettled;
  const imageLoaded = new Promise(resolve => { imageSettled = resolve; });
  const failure = () => new Response(null, { status: 502, headers: { 'X-MonKado-Render-Stage': stage, 'X-MonKado-Origin-Status': String(originStatus || 0) } });
  let next = 0, requests = 0, networkBytes = 0, imageRequests = 0;
  const pending = new Map(), hosts = new Map();
  let settle;
  const loaded = new Promise(resolve => { settle = resolve; });
  const abort = () => settle(false);
  signal.addEventListener('abort', abort, { once: true });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    if (signal.aborted || pending.size >= 128) return reject(new Error('Browser budget exhausted'));
    const id = ++next;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Browser command unavailable')); }, 1800);
    pending.set(id, {
      resolve: value => { clearTimeout(timer); resolve(value); },
      reject: error => { clearTimeout(timer); reject(error); },
    });
    try { socket.send(JSON.stringify({ id, method, params })); }
    catch { const item = pending.get(id); pending.delete(id); item.reject(new Error('Browser connection unavailable')); }
  });
  const guardResource = async event => {
    if (event.redirectedRequestId) throw new Error('Redirect rejected');
    const target = safeBrowserURL(event.request.url);
    if (event.responseStatusCode !== undefined) {
      if (event.resourceType !== 'Document' || event.responseStatusCode >= 300 && event.responseStatusCode < 400) throw new Error('Document response rejected');
      await send('Fetch.continueResponse', { requestId: event.requestId, responseCode:event.responseStatusCode,
        responseHeaders:[...(event.responseHeaders || []),{name:'Content-Security-Policy',value:browserCSP}] });
      return;
    }
    const method = event.request.method;
    const allowedMethod = method === 'GET' || method === 'POST' && typeof event.request.postData === 'string' && new TextEncoder().encode(event.request.postData).length <= 65536;
    requests++;
    const selectedImage = event.resourceType === 'Image' && target.href === imageURL;
    if (selectedImage) imageRequests++;
    const allowed = !signal.aborted && allowedMethod && (selectedImage && imageRequests === 1 || !selectedImage && requests <= maximumRequests &&
      ['Document', 'Script', 'Stylesheet', 'XHR', 'Fetch', 'Other'].includes(event.resourceType));
    if (!allowed) throw new Error('Resource rejected');
    if (!hosts.has(target.hostname)) {
      if (hosts.size >= maximumHosts) throw new Error('Host limit');
      hosts.set(target.hostname, validateDNS(target.hostname, upstream, signal));
    }
    await hosts.get(target.hostname);
    signal.throwIfAborted();
    await send('Fetch.continueRequest', { requestId: event.requestId });
  };
  try {
    const limits = await env.BROWSER.limits();
    signal.throwIfAborted();
    if (route === '/limits') return Response.json({ usedSeconds: limits.usedBrowserTimeSeconds, active: limits.activeSessions.length, acquisitions: limits.allowedBrowserAcquisitions });
    // Fail fast instead of queueing through the free tier's acquisition window.
    if (!Number.isFinite(limits.usedBrowserTimeSeconds) || limits.usedBrowserTimeSeconds > freeBrowserReserve || limits.activeSessions.length || limits.allowedBrowserAcquisitions < 1) {
      return new Response(null, { status: 429, headers: { 'Retry-After': '20' } });
    }
    let target;
    try {
      const input = JSON.parse(new TextDecoder().decode(await bounded(request.body, 8192, signal)));
      if (!input || typeof input.url !== 'string' || Object.keys(input).some(key => key !== 'url') || /[\s\\]/.test(input.url)) throw new Error('URL rejected');
      target = safeBrowserURL(input.url);
    } catch { return new Response(null, { status: 400 }); }
    stage = 'initial-dns';
    await validateDNS(target.hostname, upstream, signal);
    hosts.set(target.hostname, Promise.resolve());
    signal.throwIfAborted();
    stage = 'acquire';
    session = await env.BROWSER.acquire({ keepAlive: 10000, targets: true });
    signal.throwIfAborted();
    let page = (session.targets || []).find(item => item.type === 'page');
    if (!page) page = await env.BROWSER.devtools.newTarget(session.sessionId);
    stage = 'connect';
    const connection = await env.BROWSER.connectSession(session.sessionId, { targetId: page.id });
    const upgraded = await connection.webSocket.fetch('https://browser-binding.invalid', { headers: { Upgrade: 'websocket' } });
    socket = upgraded.webSocket;
    if (!socket) throw new Error('Browser connection unavailable');
    socket.accept();
    socket.addEventListener('message', event => {
      try {
        if (typeof event.data !== 'string' || event.data.length > 2 * htmlMaximum + 32768) throw new Error('Protocol body limit');
        const message = JSON.parse(event.data);
        if (message.id) {
          const item = pending.get(message.id);
          if (item) { pending.delete(message.id); message.error ? item.reject(new Error('Browser command failed')) : item.resolve(message.result); }
          return;
        }
        const data = message.params || {};
        if (message.method === 'Network.requestWillBeSent' && data.redirectResponse) stop.abort();
        if (message.method === 'Page.domContentEventFired') settle(true);
        if (message.method === 'Network.responseReceived' && data.type === 'Document' && data.frameId === mainFrame) {
          originStatus = data.response.status; finalURL = data.response.url;
        }
        if (message.method === 'Network.responseReceived' && data.type === 'Image' && data.response.url === imageURL) {
          imageRequest = data.requestId;
          imageMedia = data.response.mimeType;
          if (data.response.status !== 200 || !['image/jpeg', 'image/png', 'image/webp', 'image/avif'].includes(imageMedia)) imageSettled(false);
        }
        if (message.method === 'Network.loadingFailed' && data.requestId === imageRequest) imageSettled(false);
        if (message.method === 'Network.loadingFinished' && data.requestId === imageRequest) imageSettled(data.encodedDataLength <= imageMaximum);
        if (message.method === 'Network.dataReceived') {
          networkBytes += data.encodedDataLength || data.dataLength || 0;
          if (networkBytes > networkMaximum) stop.abort();
        }
        if (message.method === 'Fetch.requestPaused') {
          void guardResource(data).catch(async () => {
            try { await send('Fetch.failRequest', { requestId: data.requestId, errorReason: 'BlockedByClient' }); }
            catch { settle(false); }
          });
        }
      } catch { stop.abort(); }
    });
    stage = 'configure';
    await send('Page.enable');
    await send('Network.enable');
    await send('Network.setCacheDisabled', { cacheDisabled: true });
    await send('Network.setBypassServiceWorker', { bypass:true });
    await send('Network.clearBrowserCookies');
    mainFrame = (await send('Page.getFrameTree')).frameTree.frame.id;
    await send('Network.setBlockedURLs', { urls: ['ws://*', 'wss://*', 'http://*', 'file://*'] });
    await send('Network.setExtraHTTPHeaders', { headers: { 'Accept-Language': 'fr-FR,fr;q=0.9' } });
    await send('Emulation.setScriptExecutionDisabled', { value: false });
    await send('Fetch.enable', { patterns: [{ urlPattern: '*', requestStage: 'Request' },{urlPattern:'*',resourceType:'Document',requestStage:'Response'}] });
    timeout = setTimeout(() => settle(false), 4000);
    stage = 'navigate';
    await send('Page.navigate', { url: target.href });
    if (!await loaded) throw new Error('Browser navigation unavailable');
    signal.throwIfAborted();
    stage = 'metadata';
    await send('Runtime.evaluate', { expression: readyExpression, awaitPromise: true, returnByValue: true });
    const result = await send('Runtime.evaluate', { expression: 'JSON.stringify({html:document.documentElement.outerHTML,url:location.href,imageURL:document.querySelector(\'meta[property="og:image"]\')?.content})', returnByValue: true });
    const document = JSON.parse(result.result.value);
    if (originStatus !== 200 || finalURL !== target.href || document.url !== target.href || typeof document.html !== 'string' || new TextEncoder().encode(document.html).length > htmlMaximum) {
      return failure();
    }
    // Fetch one declared preview picture in this anonymous session. Reuse avoids
    // another browser acquisition and its free-tier waiting window.
    let image;
    const remaining = Math.min(1000, browserTimeout - (Date.now() - started) - 500);
    if (typeof document.imageURL === 'string' && document.imageURL.length <= 2048 && remaining > 100) {
      let imageTimer;
      try {
        imageURL = safeBrowserURL(new URL(document.imageURL, target).href).href;
        const picture = (async () => {
          await send('Runtime.evaluate', { expression: `globalThis.__monkadoPreviewImage=new Image();globalThis.__monkadoPreviewImage.src=${JSON.stringify(imageURL)};void 0`, returnByValue: true });
          if (!await imageLoaded) return;
          const body = await send('Network.getResponseBody', { requestId: imageRequest });
          if (body.base64Encoded && typeof body.body === 'string' && body.body.length <= Math.ceil(imageMaximum / 3) * 4) return { url: imageURL, contentType: imageMedia, contentBase64: body.body };
        })();
        image = await Promise.race([picture, new Promise(resolve => { imageTimer = setTimeout(resolve, remaining); })]);
      } catch { /* Keep usable product text when the optional picture is unavailable. */ }
      finally { clearTimeout(imageTimer); }
    }
    stage = 'final-dns';
    await Promise.all(hosts.values());
    for (const host of hosts.keys()) await validateDNS(host, upstream, signal);
    signal.throwIfAborted();
    return Response.json({ html: document.html, url: document.url, statusCode: originStatus, ...(image ? { image } : {}) }, { headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' } });
  } catch { return failure(); }
  finally {
    clearTimeout(timeout);
    signal.removeEventListener('abort', abort);
    for (const item of pending.values()) item.reject(new Error('Browser closed'));
    pending.clear();
    try { socket?.close(); } catch { /* The owned session is still closed below. */ }
    if (session) {
      const close = env.BROWSER.closeSession(session.sessionId).catch(() => {});
      execution.waitUntil(close);
      await close;
    }
  }
}

export default { fetch(request, env, execution) { return handleRender(request, env, execution); } };
