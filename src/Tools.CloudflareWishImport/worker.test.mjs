import { test } from 'node:test';
import assert from 'node:assert/strict';
import { handle, publicAddress } from './worker.mjs';


const env = { TEST_AUTH:'a'.repeat(64) };
const source = 'https://www.placedeslibraires.fr/livre/example/';
function imageRequest(url='https://media.merchant.fr/photo',headers={}) {
  return new Request('http://127.0.0.1/image',{method:'POST',headers:{Authorization:`Bearer ${env.TEST_AUTH}`,'Content-Type':'application/json',...headers},body:JSON.stringify({url})});
}
function request(url = source, headers = {}) {
  return new Request('http://127.0.0.1/preview',{method:'POST',headers:{Authorization:`Bearer ${env.TEST_AUTH}`,'Content-Type':'application/json',...headers},body:JSON.stringify({url})});
}
function network(merchant, addresses = ['93.184.216.34']) {
  return async (url, options) => {
    if (new URL(url).hostname === 'cloudflare-dns.com') {
      assert.equal(options.redirect,'manual');
      assert.deepEqual(options.headers,{Accept:'application/dns-json'});
      const ipv6 = new URL(url).searchParams.get('type') === 'AAAA';
      return Response.json({Status:0,Answer:addresses.filter(address=>address.includes(':')===ipv6).map(data=>({type:ipv6?28:1,data}))});
    }
    return merchant(url,options);
  };
}

test('Image relay returns bounded bytes without forwarding credentials or cookies',async()=>{
  let calls=0,dns=0;
  const upstream=network(async(url,options)=>{
    calls++;assert.equal(url,'https://media.merchant.fr/photo');assert.equal(options.redirect,'manual');
    assert.deepEqual(Object.keys(options.headers).sort(),['Accept','User-Agent']);
    assert.equal(options.headers.Accept,'image/jpeg,image/png,image/webp,image/avif');
    return new Response(new Uint8Array([1,2,3]),{headers:{'Content-Type':'image/avif','Set-Cookie':'private'}});
  });
  const response=await handle(imageRequest(undefined,{Cookie:'private=member'}),env,async(...args)=>{if(new URL(args[0]).hostname==='cloudflare-dns.com')dns++;return upstream(...args)});
  assert.equal(response.status,200);assert.deepEqual([...new Uint8Array(await response.arrayBuffer())],[1,2,3]);
  assert.equal(calls,1);assert.equal(dns,4);assert.equal(response.headers.get('Set-Cookie'),null);
});

test('Image relay refuses redirects, active content, oversized bodies and DNS rebinding',async()=>{
  for(const origin of [
    ()=>new Response(null,{status:302,headers:{Location:'https://127.0.0.1/'}}),
    ()=>new Response('<svg/>',{headers:{'Content-Type':'image/svg+xml'}}),
    ()=>new Response('<html/>',{headers:{'Content-Type':'text/html'}}),
    ()=>new Response('x',{headers:{'Content-Type':'image/png','Content-Length':String(10*1024*1024+1)}}),
    ()=>new Response(new Uint8Array(10*1024*1024+1),{headers:{'Content-Type':'image/jpeg'}}),
  ]){let calls=0;const response=await handle(imageRequest(),env,network(async()=>{calls++;return origin()}));assert.equal(response.status,502);assert.equal(calls,1);}
  let addresses=0;
  const response=await handle(imageRequest(),env,async(url)=>{
    if(new URL(url).hostname==='cloudflare-dns.com'){
      const type=new URL(url).searchParams.get('type');if(type==='A')addresses++;
      return Response.json({Status:0,Answer:type==='A'?[{type:1,data:addresses>1?'127.0.0.1':'93.184.216.34'}]:[]});
    }
    return new Response('photo',{headers:{'Content-Type':'image/png'}});
  });
  assert.equal(response.status,502);
  assert.equal((await handle(imageRequest('https://127.0.0.1/'),env,()=>assert.fail('Unsafe URL reached network'))).status,400);
});

test('Public DNS classification rejects private, special and mapped addresses',()=>{
  for (const address of ['0.0.0.0','10.1.2.3','100.64.1.1','127.0.0.1','169.254.169.254','172.16.0.1','192.0.0.1','192.0.2.1','192.168.0.1','198.18.1.1','198.51.100.1','203.0.113.1','224.0.0.1','255.255.255.255','::','::1','::ffff:127.0.0.1','::ffff:8.8.8.8','fc00::1','fe80::1','ff02::1','2001::1','2001:db8::1','2002::1','3fff::1','2001:4860::1%1','not-an-ip']) assert.equal(publicAddress(address),false,address);
  for (const address of ['8.8.8.8','188.114.96.6','93.184.216.34','2001:4860:4860::8888','2606:4700::1111']) assert.equal(publicAddress(address),true,address);
});
test('Unauthorized requests and invalid URL authorities never contact any network',async()=>{
  for(const input of [request(source,{Authorization:'Bearer wrong'}),...['http://www.placedeslibraires.fr/','https://www.placedeslibraires.fr.evil.example/','https://www.placedeslibraires.fr./','https://127.0.0.1/','https://[::1]/','https://www.placedeslibraires.fr:8443/','https://name:password@www.placedeslibraires.fr/','https://www.fnac.com\\@127.0.0.1/'].map(url=>request(url))]) {
    const result=await handle(input,env,()=>assert.fail('Unexpected network request'));
    assert.ok([400,401].includes(result.status));
  }
});

test('First-ever public merchant uses Cloudflare with all DNS protections intact',async()=>{
  for(const host of ['www.new-merchant.fr','shop.independent-store.com','www.cultura.com','www.boulanger.com','www.leroymerlin.fr','www.maisonsdumonde.com']){
    let calls=0;
    const result=await handle(request('https://'+host+'/product'),env,network(async(url)=>{calls++;assert.equal(new URL(url).hostname,host);return new Response('<title>Product</title>',{headers:{'Content-Type':'text/html'}})}));
    assert.equal(result.status,200,host);assert.equal(calls,1);
    for(const addresses of [['93.184.216.34','10.0.0.1'],['127.0.0.1'],['::ffff:8.8.8.8'],[]]){
      assert.equal((await handle(request('https://'+host+'/product'),env,network(()=>assert.fail('Rejected DNS reached merchant'),addresses))).status,403);
    }
  }
  for(const url of ['https://169.254.169.254/','https://metadata.google.internal/','https://merchant.local/','https://merchant.home.arpa/','https://merchant.onion/','https://merchant.fr:8443/','http://merchant.fr/','https://user:secret@merchant.fr/']){
    assert.equal((await handle(request(url),env,()=>assert.fail('Invalid authority reached network'))).status,400,url);
  }
});
test('Mixed, private, empty or failed DNS answers block merchant retrieval',async()=>{
  for(const addresses of [['93.184.216.34','10.0.0.1'],['::ffff:127.0.0.1'],[],['2001:db8::1']]) {
    const result=await handle(request(),env,network(()=>assert.fail('Merchant contacted after rejected DNS'),addresses));
    assert.equal(result.status,403);
  }
  for(const response of [()=>new Response(null,{status:302,headers:{Location:'https://127.0.0.1/'}}),()=>new Response(null,{status:503}),()=>Response.json({Status:2}),()=>Response.json({Status:0,Answer:'invalid'}),()=>new Response('invalid JSON')]) {
    const result=await handle(request(),env,async(url)=>{assert.equal(new URL(url).hostname,'cloudflare-dns.com');return response()});
    assert.equal(result.status,403);
  }
});
test('Successful HTML transfer sends no user cookies or credentials to the merchant',async()=>{
  let calls=0;
  const result=await handle(request(source+'#private',{Cookie:'member=private'}),env,network(async(url,options)=>{
    calls++;assert.equal(url,source);assert.equal(options.redirect,'manual');
    assert.deepEqual(Object.keys(options.headers),['User-Agent']);
    return new Response('<title>Product</title>',{headers:{'Content-Type':'text/html; charset=utf-8','Set-Cookie':'merchant=private'}});
  }));
  assert.equal(result.status,200);assert.equal(calls,1);assert.equal(await result.text(),'<title>Product</title>');
  assert.equal(result.headers.get('Set-Cookie'),null);assert.equal(result.headers.get('Cache-Control'),'no-store');
});
test('Redirects and permanent failures are never followed or retried',async()=>{
  for(const status of [301,302,307,403,429,503]) {
    let calls=0;
    const result=await handle(request(),env,network(async()=>{calls++;return new Response(null,{status,headers:{Location:'https://127.0.0.1/'}})}));
    assert.equal(result.status,502);assert.equal(calls,1);
  }
});

test('Merchant status and safe wait instructions remain available for relay retry classification',async()=>{
  for(const value of ['60','Wed, 28 Oct 2026 10:00:00 GMT']){
    const response=await handle(request(),env,network(async()=>new Response(null,{status:503,headers:{'Retry-After':value}})));
    assert.equal(response.status,502);
    assert.equal(response.headers.get('X-MonKado-Origin-Status'),'503');
    assert.equal(response.headers.get('X-MonKado-Origin-Retry-After'),value==='60'?'60':'later');
  }
});
test('Declared and streamed response sizes, media and request sizes fail closed',async()=>{
  for(const response of [()=>new Response('binary',{headers:{'Content-Type':'image/png'}}),()=>new Response('x',{headers:{'Content-Type':'text/html','Content-Length':'4194305'}}),()=>new Response(new Uint8Array(4194305),{headers:{'Content-Type':'text/html'}})]) {
    const result=await handle(request(),env,network(async()=>response()));assert.equal(result.status,502);
  }
  assert.equal((await handle(request(source+'a'.repeat(9000)),env,()=>assert.fail('Oversize request reached network'))).status,400);
});



test('Permanent relay secret takes precedence and malformed bindings fail closed',async()=>{
  const production={IMPORT_AUTH:'b'.repeat(64),TEST_AUTH:env.TEST_AUTH};
  let calls=0;const upstream=network(async()=>{calls++;return new Response('<title>Notebook</title>',{headers:{'Content-Type':'text/html'}})});
  const accepted=await handle(request(source,{Authorization:'Bearer '+production.IMPORT_AUTH}),production,upstream);
  assert.equal(accepted.status,200);assert.equal(calls,1);
  const refused=await handle(request(),production,()=>assert.fail('Legacy key reached the network'));
  assert.equal(refused.status,401);
  assert.equal((await handle(request(),{...production,IMPORT_AUTH:''},()=>assert.fail('Malformed key reached the network'))).status,401);
});

test('HTML is withheld if merchant DNS becomes private after retrieval',async()=>{
  let retrieved=false;
  const upstream=async(url,options)=>{
    if(new URL(url).hostname==='cloudflare-dns.com'){
      const ipv6=new URL(url).searchParams.get('type')==='AAAA';
      return Response.json({Status:0,Answer:ipv6?[]:[{type:1,data:retrieved?'127.0.0.1':'93.184.216.34'}]});
    }
    retrieved=true;return new Response('<title>Private after fetch</title>',{headers:{'Content-Type':'text/html'}});
  };
  const result=await handle(request(),env,upstream);
  assert.equal(result.status,502);assert.equal(await result.text(),'');
});
