import { test } from 'node:test';
import assert from 'node:assert/strict';
import { handleRender } from './browser-worker.mjs';

const secret = 'a'.repeat(64);
const target = 'https://merchant.fr/product?size=large';
const html = '<title>Notebook</title><meta property="og:price:amount" content="12.30"><meta property="og:price:currency" content="EUR">';
const request = (url = target, headers = {}) => new Request('https://relay.invalid/render', {
  method: 'POST', headers: { Authorization: `Bearer ${secret}`, 'Content-Type': 'application/json', ...headers }, body: JSON.stringify({ url }),
});
function fixture(options = {}) {
  const calls = [], commands = [], continued = [], denied = [];
  const state = { closed: [], acquired: 0, dns: 0 };
  let address;
  class Socket extends EventTarget {
    accept() {}
    close() { state.socketClosed = true; }
    emit(message) { this.dispatchEvent(Object.assign(new Event('message'), { data: JSON.stringify(message) })); }
    send(raw) {
      const { id, method, params } = JSON.parse(raw); commands.push({ method, params });
      if (options.commandFailure === method) throw new Error('simulated command failure');
      queueMicrotask(() => {
        let result = {};
        if (method === 'Page.getFrameTree') result = { frameTree: { frame: { id: 'main' } } };
        if (method === 'Fetch.continueRequest') continued.push(params.requestId);
        if (method === 'Fetch.failRequest') denied.push(params.requestId);
        if (method === 'Runtime.evaluate') result = { result: { value: params.awaitPromise ? true : JSON.stringify({ html: options.html ?? html, url: options.finalURL ?? address, imageURL:options.imageURL }) } };
        if (method === 'Network.getResponseBody') result = { base64Encoded:true, body:options.imageBody ?? 'aW1hZ2U=' };
        this.emit({ id, result });
        if (method === 'Runtime.evaluate' && params.expression.startsWith('globalThis.__monkadoPreviewImage')) {
          this.emit({method:'Fetch.requestPaused',params:{requestId:'selected-image',resourceType:'Image',request:{url:options.imageURL,method:'GET'}}});
          this.emit({method:'Network.responseReceived',params:{type:'Image',requestId:'image-body',response:{url:options.imageURL,status:options.imageStatus ?? 200,mimeType:options.imageMedia ?? 'image/jpeg'}}});
          this.emit({method:'Network.loadingFinished',params:{requestId:'image-body',encodedDataLength:options.imageSize ?? 5}});
        }
        if (method === 'Page.navigate') {
          address = params.url;
          if (options.resources) for (const resource of options.resources) this.emit({ method: 'Fetch.requestPaused', params: resource });
          this.emit({ method: 'Network.responseReceived', params: { type: 'Document', frameId: 'main', response: { status: options.status ?? 200, url: options.finalURL ?? address } } });
          if (options.excessiveNetwork) this.emit({ method: 'Network.dataReceived', params: { dataLength: 8 * 1024 * 1024 + 1 } });
          options.abort?.abort();
          this.emit({ method: 'Page.domContentEventFired' });
        }
      });
    }
  }
  const socket = new Socket();
  const env = { IMPORT_AUTH: secret, BROWSER: {
    limits: async () => options.limits ?? { usedBrowserTimeSeconds: 0, activeSessions: [], allowedBrowserAcquisitions: 1 },
    acquire: async config => { state.acquired++; assert.deepEqual(config, { keepAlive: 10000, targets: true }); return { sessionId: 'owned', targets: [{ id: 'page', type: 'page' }] }; },
    connectSession: async (id, config) => { assert.equal(id, 'owned'); assert.deepEqual(config, { targetId: 'page' }); return { webSocket: { fetch: async () => ({ webSocket: socket }) } }; },
    closeSession: async id => { state.closed.push(id); },
  } };
  const upstream = async (url, config) => {
    calls.push(url); assert.equal(new URL(url).hostname, 'cloudflare-dns.com'); assert.equal(config.redirect, 'manual'); state.dns++;
    const type = new URL(url).searchParams.get('type');
    return Response.json({ Status: 0, Answer: type === 'A' ? [{ type: 1, data: options.privateDNS || options.rebind && state.dns > 2 ? '127.0.0.1' : '93.184.216.34' }] : [] });
  };
  return { env, upstream, state, commands, continued, denied };
}

test('Browser relay authenticates before quota checks and refuses malformed authorities', async () => {
  const f = fixture();
  assert.equal((await handleRender(request(target, { Authorization: 'Bearer wrong' }), f.env, undefined, () => assert.fail('network'))).status, 401);
  for (const value of ['https://127.0.0.1/', 'https://metadata.google.internal/', 'http://merchant.fr/', 'https://user:secret@merchant.fr/', 'https://merchant.fr:8443/', 'https://merchant.fr\\@127.0.0.1/']) {
    assert.equal((await handleRender(request(value), f.env, undefined, () => assert.fail('network'))).status, 400);
  }
  assert.equal(f.state.acquired, 0);
  assert.equal((await handleRender(request(target, { 'Content-Type': 'text/plain' }), f.env)).status, 415);
  assert.equal((await handleRender(new Request('https://relay.invalid/render', { headers: { Authorization: `Bearer ${secret}` } }), f.env)).status, 404);
});

test('Free browser quota or existing sessions fail immediately without launching or waiting', async () => {
  for (const limits of [
    { usedBrowserTimeSeconds: 501, activeSessions: [], allowedBrowserAcquisitions: 1 },
    { usedBrowserTimeSeconds: 1, activeSessions: [{ id: 'another-owner' }], allowedBrowserAcquisitions: 1 },
    { usedBrowserTimeSeconds: 1, activeSessions: [], allowedBrowserAcquisitions: 0 },
    { usedBrowserTimeSeconds: NaN, activeSessions: [], allowedBrowserAcquisitions: 1 },
  ]) {
    const f = fixture({ limits }); const response = await handleRender(request(), f.env, undefined, () => assert.fail('network'));
    assert.equal(response.status, 429); assert.equal(f.state.acquired, 0); assert.equal(response.headers.get('Retry-After'), '20');
  }
});

test('Public rendered metadata preserves the exact query, clears user fragments and closes only its owned session', async () => {
  const f = fixture();
  const response = await handleRender(request(target + '#private', { Cookie: 'member-secret=never-forward' }), f.env, undefined, f.upstream);
  assert.equal(response.status, 200); assert.deepEqual(await response.json(), { html, url: target, statusCode: 200 });
  assert.equal(response.headers.get('Cache-Control'), 'no-store'); assert.deepEqual(f.state.closed, ['owned']);
  assert.ok(f.state.socketClosed); assert.equal(f.state.dns, 4);
  assert.ok(f.commands.some(command => command.method === 'Network.clearBrowserCookies'));
  assert.deepEqual(f.commands.find(command => command.method === 'Network.setExtraHTTPHeaders').params.headers, { 'Accept-Language': 'fr-FR,fr;q=0.9' });
});

test('A challenge, changed product, oversized output, rebinding or cancellation never exposes HTML', async () => {
  for (const options of [{ status: 403 }, { finalURL: 'https://merchant.fr/' }, { html: 'x'.repeat(4 * 1024 * 1024 + 1) }, { rebind: true }, { privateDNS: true }, { excessiveNetwork: true }, { commandFailure: 'Page.enable' }]) {
    const f = fixture(options), response = await handleRender(request(), f.env, undefined, f.upstream);
    assert.equal(response.status, 502); assert.equal(await response.text(), '');
    assert.equal(f.state.closed.length, f.state.acquired);
  }
  const abort = new AbortController(), f = fixture({ abort });
  const input = new Request('https://relay.invalid/render', { method: 'POST', headers: { Authorization: `Bearer ${secret}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ url: target }), signal: abort.signal });
  assert.equal((await handleRender(input, f.env, undefined, f.upstream)).status, 502); assert.deepEqual(f.state.closed, ['owned']);
});

test('Every resource is guarded before continuing; credentials, private authorities and unsafe methods are denied', async () => {
  const resources = [
    {requestId:'document-response',resourceType:'Document',request:{url:target,method:'GET'},responseStatusCode:200,responseHeaders:[{name:'Content-Type',value:'text/html'}]},
    { requestId: 'public-script', resourceType: 'Script', request: { url: 'https://assets.merchant.fr/main.js', method: 'GET' } },
    { requestId: 'private', resourceType: 'XHR', request: { url: 'https://127.0.0.1/', method: 'GET' } },
    { requestId: 'credentials', resourceType: 'XHR', request: { url: 'https://user:secret@merchant.fr/', method: 'GET' } },
    { requestId: 'delete', resourceType: 'Fetch', request: { url: target, method: 'DELETE' } },
    { requestId: 'large-post', resourceType: 'Fetch', request: { url: target, method: 'POST', postData: 'x'.repeat(65537) } },
    { requestId: 'image', resourceType: 'Image', request: { url: 'https://assets.merchant.fr/photo', method: 'GET' } },
  ];
  const f = fixture({ resources });
  assert.equal((await handleRender(request(), f.env, undefined, f.upstream)).status, 200);
  assert.deepEqual(f.continued, ['public-script']);
  assert.deepEqual(f.denied.sort(), ['private', 'credentials', 'delete', 'large-post', 'image'].sort());
  const sandbox=f.commands.find(c=>c.method==='Fetch.continueResponse').params.responseHeaders.find(h=>h.name==='Content-Security-Policy').value;
  assert.match(sandbox,/worker-src 'none'/);assert.match(sandbox,/sandbox allow-scripts allow-same-origin/);assert.ok(!sandbox.includes('allow-popups'));
  assert.deepEqual(f.commands.find(c=>c.method==='Network.setBypassServiceWorker').params,{bypass:true});
});

test('The declared preview image reuses its owned browser with bounded bytes and no second acquisition',async()=>{
 const imageURL='https://assets.merchant.fr/photo.jpg',f=fixture({imageURL});
 const response=await handleRender(request(),f.env,undefined,f.upstream),result=await response.json();
 assert.equal(response.status,200);assert.deepEqual(result.image,{url:imageURL,contentType:'image/jpeg',contentBase64:'aW1hZ2U='});
 assert.equal(f.state.acquired,1);assert.deepEqual(f.state.closed,['owned']);assert.deepEqual(f.continued,['selected-image']);
 for(const options of [{imageStatus:403},{imageMedia:'image/svg+xml'},{imageSize:2*1024*1024+1}]){
  const bad=fixture({imageURL,...options}),r=await handleRender(request(),bad.env,undefined,bad.upstream);
  assert.equal(r.status,200);assert.equal((await r.json()).image,undefined);assert.deepEqual(bad.state.closed,['owned']);
 }
});
