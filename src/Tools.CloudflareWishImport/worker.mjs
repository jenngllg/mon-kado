import { BlockList, isIP } from 'node:net';
import { timingSafeEqual } from 'node:crypto';

// Authenticated relay. Workers controls final DNS resolution; this is not IP pinning.
// Production use requires the explicitly authorized managed-network exception.

function merchantHost(host) {
  if (!/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$/.test(host) || isIP(host)) return false;
  return !['localhost','local','internal','invalid','test','example','onion','home.arpa'].some(suffix => host.endsWith('.'+suffix));
}
const htmlMaximum = 4 * 1024 * 1024;
const imageMaximum = 10 * 1024 * 1024;
const timeout = 4500;
const denied = new BlockList();
const globalIPv6 = new BlockList();
globalIPv6.addSubnet('2000::', 3, 'ipv6');
for (const cidr of ['0.0.0.0/8','10.0.0.0/8','100.64.0.0/10','127.0.0.0/8','169.254.0.0/16','172.16.0.0/12','192.0.0.0/24','192.0.2.0/24','192.88.99.0/24','192.168.0.0/16','198.18.0.0/15','198.51.100.0/24','203.0.113.0/24','224.0.0.0/3','2001::/23','2001:db8::/32','2002::/16','3fff::/20']) {
  const [address,prefix] = cidr.split('/');
  denied.addSubnet(address, Number(prefix), address.includes(':') ? 'ipv6' : 'ipv4');
}

/** Reject private, mapped, non-global and special-purpose DNS answers. */
export function publicAddress(address) {
  if (typeof address !== 'string' || address.includes('%')) return false;
  const family = isIP(address);
  if (!family) return false;
  if (family === 6 && !globalIPv6.check(address, 'ipv6')) return false;
  return !denied.check(address, family === 6 ? 'ipv6' : 'ipv4');
}

async function bounded(body, maximum, signal) {
  if (!body) throw new Error('Unavailable body');
  const reader = body.getReader();
  const abort = () => { reader.cancel().catch(() => {}); };
  signal.addEventListener('abort', abort, { once: true });
  const chunks = []; let length = 0;
  try {
    for (;;) {
      signal.throwIfAborted();
      const {done,value} = await reader.read();
      signal.throwIfAborted();
      if (done) break;
      length += value.byteLength;
      if (length > maximum) throw new Error('Body limit');
      chunks.push(value);
    }
  } catch(error) { await reader.cancel(); throw error; }
  finally { signal.removeEventListener('abort', abort); reader.releaseLock(); }
  const content = new Uint8Array(length); let offset = 0;
  for (const chunk of chunks) { content.set(chunk, offset); offset += chunk.byteLength; }
  return content;
}

async function validateDNS(host, upstream, signal) {
  const answers = await Promise.all(['A','AAAA'].map(async type => {
    const address = new URL('https://cloudflare-dns.com/dns-query');
    address.searchParams.set('name',host); address.searchParams.set('type',type);
    let response;
    try { response = await upstream(address.href,{method:'GET',redirect:'manual',signal,headers:{Accept:'application/dns-json'}}); }
    catch(error) { throw new Error('DNS_FETCH_'+String(error.name).replace(/[^A-Za-z]/g,'').toUpperCase()); }
    if (!response.ok) { await response.body?.cancel(); throw new Error('DNS_HTTP_'+response.status); }
    let raw;
    try { raw = await bounded(response.body,16384,signal); }
    catch(error) { throw new Error('DNS_BODY_'+String(error.name).replace(/[^A-Za-z]/g,'').toUpperCase()); }
    let data;
    try { data = JSON.parse(new TextDecoder().decode(raw)); }
    catch { throw new Error('DNS_JSON_INVALID'); }
    if (data.Status !== 0 || (data.Answer !== undefined && !Array.isArray(data.Answer))) throw new Error('DNS_FORMAT');
    const records = data.Answer || [];
    if (records.length > 64) throw new Error('DNS_LIMIT');
    return records.filter(record => record.type === 1 || record.type === 28).map(record => record.data);
  }));
  const addresses = answers.flat();
  if (!addresses.length) throw new Error('DNS_EMPTY');
  let accepted;
  try { accepted = addresses.every(publicAddress); }
  catch(error) { throw new Error('DNS_CLASSIFY_'+String(error.name).replace(/[^A-Za-z]/g,'').toUpperCase()); }
  if (!accepted) throw new Error('DNS_REJECTED');
  // This is a public-DNS preflight, not a guarantee about the IP later chosen by fetch.
}

function authenticated(request, env) {
  const key=env.IMPORT_AUTH ?? env.TEST_AUTH;
  if (typeof key !== 'string' || !/^[a-f0-9]{64}$/.test(key)) return false;
  const received = new TextEncoder().encode(request.headers.get('Authorization') || '');
  const expected = new TextEncoder().encode(`Bearer ${key}`);
  return received.byteLength === expected.byteLength && timingSafeEqual(received, expected);
}

/** Fetch bounded passive product HTML or image bytes for the authenticated API only. */
export async function handle(request, env, upstream = fetch) {
  if (!authenticated(request,env)) return new Response(null,{status:401});
  const route=new URL(request.url).pathname;
  if (request.method !== 'POST' || !['/preview','/image'].includes(route)) return new Response(null,{status:404});
  const image=route==='/image',maximum=image?imageMaximum:htmlMaximum;
  if (request.headers.get('Content-Type') !== 'application/json') return new Response(null,{status:415});
  const signal = AbortSignal.any([request.signal,AbortSignal.timeout(timeout)]);
  let target;
  try {
    const input = JSON.parse(new TextDecoder().decode(await bounded(request.body,8192,signal)));
    if (!input || typeof input.url !== 'string' || Object.keys(input).some(key => key !== 'url') || /[\s\\]/.test(input.url)) throw new Error('URL rejected');
    target = new URL(input.url);
    if (target.protocol !== 'https:' || target.username || target.password || target.port || !merchantHost(target.hostname)) throw new Error('URL rejected');
    target.hash = '';
  } catch { return new Response(null,{status:400}); }
  try { signal.throwIfAborted(); await validateDNS(target.hostname,upstream,signal); }
  catch(error) { const code=/^DNS_[A-Z0-9_]+$/.test(error.message)?error.message:'DNS_RUNTIME'; return new Response(null,{status:403,headers:{'X-MonKado-Relay-Error':code}}); }
  try {
    const headers={'User-Agent':'Go-http-client/1.1 MonKado/1.0 (+https://www.monkado.fr)'};
    if(image)headers.Accept='image/jpeg,image/png,image/webp,image/avif';
    const origin = await upstream(target.href,{method:'GET',redirect:'manual',signal,headers});
    const media = (origin.headers.get('Content-Type') || '').split(';')[0].trim().toLowerCase();
    const declared = origin.headers.get('Content-Length');
    const allowed=image?['image/jpeg','image/png','image/webp','image/avif','application/octet-stream']:['text/html','application/xhtml+xml'];
    if (origin.status !== 200 || !allowed.includes(media) || (declared !== null && (!/^\d+$/.test(declared) || Number(declared) > maximum))) {
      await origin.body?.cancel();
      const headers={'X-MonKado-Origin-Status':String(origin.status)};
      if(origin.headers.has('Retry-After')){
        const value=origin.headers.get('Retry-After');
        headers['X-MonKado-Origin-Retry-After']=/^\d{1,10}$/.test(value)?value:'later';
      }
      return new Response(null,{status:502,headers});
    }
    const content = await bounded(origin.body,maximum,signal);
    await validateDNS(target.hostname,upstream,signal);
    return new Response(content,{headers:{'Content-Type':origin.headers.get('Content-Type'),'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','X-MonKado-Origin-Status':'200'}});
  } catch { return new Response(null,{status:502}); }
}

export default { fetch(request,env) { return handle(request,env); } };

// Shared guards for the separately deployed, local-only browser experiment.
export { authenticated, bounded, merchantHost, validateDNS };
