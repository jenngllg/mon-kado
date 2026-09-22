// Local-only k6 entry point. No remote imports, cloud output or request debug logs.
import http from 'k6/http';
import exec from 'k6/execution';
import { fail } from 'k6';
import { Counter, Trend, Rate } from 'k6/metrics';

const origin = 'https://mk816.test';
const profile = __ENV.PROFILE;
const fixture = JSON.parse(open('/fixture/fixtures-' + __ENV.SHARD + '.json'));
const image = open('/fixture/image.png', 'b');
const duration = new Trend('business_ms', true);
const outcome = new Counter('business_ok');
const failures = new Rate('business_failure');
const status = new Trend('business_status');
const auxiliary = new Counter('auxiliary_requests');
const setupFailure = new Counter('setup_failures');
const readOnly = ['images', 'exports', 'quotas', 'concurrency'].includes(profile);
const families = readOnly
    ? ['lists', 'lists', 'lists', 'lists', 'wishes', 'wishes', 'wishes', 'shared', 'shared', 'shared']
    : ['lists', 'lists', 'lists', 'wishes', 'wishes', 'shared', 'shared', 'shared', 'update', 'reservation'];
const warmup = profile === 'smoke' ? 0 : 180;
const measureSeconds = profile === 'smoke' ? 30 : (profile === 'exports' ? 1500 :
    (['quotas', 'concurrency'].includes(profile) ? 180 : 900));
const stages = profile === 'stress'
    ? [{target: 5, duration: '300s'}, {target: 10, duration: '300s'}, {target: 20, duration: '300s'}, {target: 5, duration: '300s'}]
    : [{target: 10, duration: measureSeconds + 's'}];
// A rate of 10 per 10 seconds per shard means 10 requests/second across ten shards.
const executor = {timeUnit: '10s', preAllocatedVUs: 7, maxVUs: 7, exec: 'business', gracefulStop: '15s'};
const scenarios = profile === 'stress' ? {traffic: {
    ...executor, executor: 'ramping-arrival-rate', startRate: 10,
    stages: [{target: 10, duration: warmup + 's'}, ...stages.flatMap(stage =>
        [{target: stage.target, duration: '0s'}, stage])],
}} : {traffic: {...executor, executor: 'constant-arrival-rate',
    rate: 10, duration: (warmup + parseInt(stages[0].duration)) + 's'}};
const thresholds = {
    setup_failures: ['count==0'], dropped_iterations: ['count==0'],
    'business_failure{phase:measure}': ['rate<0.01'],
};
if (profile !== 'stress') {
    for (const family of new Set(families)) {
        thresholds['business_ms{phase:measure,family:' + family + '}'] =
            ['p(95)<' + (['update', 'reservation'].includes(family) ? '1000' : '500')];
    }
}
export const options = {
    scenarios, setupTimeout: '240s', teardownTimeout: '30s', noCookiesReset: true,
    discardResponseBodies: false, maxRedirects: 0, insecureSkipTLSVerify: false,
    systemTags: ['name', 'method', 'status', 'scenario', 'expected_response'],
    thresholds,
};

function request(actor, method, path, body, expected, name, extra = {}, multipart = false) {
    const headers = {Origin: origin, ...extra};
    if (actor.token) headers.Authorization = 'Bearer ' + actor.token;
    if (actor.csrf) headers['X-CSRF-TOKEN'] = actor.csrf;
    if (!multipart) headers['Content-Type'] = 'application/json';
    const response = http.request(method, origin + path,
        body === null ? null : (multipart ? body : JSON.stringify(body)),
        {headers, jar: actor.jar, redirects: 0, timeout: '15s', tags: {name},
            responseCallback: http.expectedStatuses(...expected)});
    return response;
}

function json(response) {
    try { return response.json(); } catch (_) { return null; }
}

function must(response, expected, name) {
    auxiliary.add(1, {family: 'auxiliary', phase: 'prepare'});
    if (!expected.includes(response.status)) {
        setupFailure.add(1, {step: name, code: String(response.status)});
        fail('FIXTURE_OR_SESSION_FAILED');
    }
    return response;
}

function csrf(actor) {
    actor.csrf = json(must(request(actor, 'GET', '/security/csrf-token', null, [200], 'csrf'), [200], 'csrf')).token;
}

function login(account) {
    const actor = {...account, jar: new http.CookieJar()};
    csrf(actor);
    const auth = json(must(request(actor, 'POST', '/api/v1/auth/sessions',
        {email: account.email, password: fixture.password, rememberMe: false}, [200], 'login'), [200], 'login'));
    actor.token = auth.accessToken;
    actor.expires = Date.now() + auth.expiresIn * 1000 - 60000;
    csrf(actor);
    return actor;
}

export function setup() {
    setupFailure.add(0);
    const actors = fixture.accounts.map(login);
    for (const actor of actors) {
        const list = actor.lists[0];
        const path = '/api/v1/wishlists/' + list.id;
        const share = json(must(request(actor, 'POST', path + '/share-link', null, [201], 'share-create'), [201], 'share-create'));
        actor.share = {id: share.id, secret: share.shareUrl.split('#')[1]};
        const wish = path + '/wishes/' + list.wishes[0];
        const before = must(request(actor, 'GET', wish, null, [200], 'wish-get'), [200], 'wish-get');
        const after = must(request(actor, 'PUT', wish + '/image',
            {image: http.file(image, 'fixture.png', 'image/png')}, [200], 'gift-image',
            {'If-Match': before.headers.Etag}, true), [200], 'gift-image');
        actor.etag = after.headers.Etag;
        const current = must(request(actor, 'GET', '/api/v1/auth/sessions/current', null, [200], 'current'), [200], 'current');
        must(request(actor, 'PUT', '/api/v1/members/current/profile/image',
            {image: http.file(image, 'fixture.png', 'image/png')}, [200], 'profile-image',
            {'If-Match': current.headers.Etag}, true), [200], 'profile-image');
    }
    for (let index = 0; index < actors.length; index++) {
        const actor = actors[index];
        const owner = actors[(index + 1) % actors.length];
        actor.shared = owner.share;
        actor.reservedWish = owner.lists[0].wishes[1];
        const path = '/api/v1/shared-wishlists/' + owner.share.id;
        const proof = {'X-MonKado-Share-Token': owner.share.secret};
        must(request(actor, 'POST', path + '/participants', {}, [201, 200], 'join', proof), [201, 200], 'join');
        const reserved = must(request(actor, 'PUT', path + '/wishes/' + actor.reservedWish + '/reservations/current',
            {quantity: 1}, [201], 'reserve', proof), [201], 'reserve');
        actor.reservationTag = reserved.headers.Etag;
        actor.reserved = true;
        actor.cookies = actor.jar.cookiesForURL(origin);
        delete actor.jar;
    }
    return actors;
}

let actor;
function session(data) {
    if (!actor) {
        actor = {...data[(exec.vu.idInInstance - 1) % 7], jar: new http.CookieJar()};
        for (const [name, values] of Object.entries(actor.cookies)) actor.jar.set(origin, name, values[0]);
    }
    if (Date.now() >= actor.expires) {
        const auth = json(must(request(actor, 'POST', '/api/v1/auth/sessions/refresh', null, [200], 'refresh'), [200], 'refresh'));
        actor.token = auth.accessToken;
        actor.expires = Date.now() + auth.expiresIn * 1000 - 60000;
        csrf(actor);
    }
    return actor;
}

export function business(data) {
    const iteration = exec.scenario.iterationInTest;
    const planned = profile === 'stress' ? 1200 : measureSeconds;
    if (iteration >= warmup + planned) return;
    const user = session(data);
    const family = families[iteration % 10];
    const phase = iteration < warmup ? 'warmup' : 'measure';
    const measuredIndex = iteration - warmup;
    let stage = 0;
    if (profile === 'stress') {
        for (const boundary of [150, 450, 1050]) {
            if (measuredIndex >= boundary) stage++;
        }
    }
    const tags = {family, phase, stage: String(stage)};
    const list = user.lists[0];
    let path = '/api/v1/wishlists';
    let method = 'GET';
    let body = null;
    let expected = [200];
    let headers = {};
    if (family === 'wishes') path += '/' + list.id + '/wishes';
    if (family === 'shared') {
        path = '/api/v1/shared-wishlists/' + user.shared.id;
        headers['X-MonKado-Share-Token'] = user.shared.secret;
    }
    if (family === 'update') {
        path += '/' + list.id + '/wishes/' + list.wishes[0];
        method = 'PUT';
        headers['If-Match'] = user.etag;
        body = {name: 'Measured gift ' + exec.scenario.iterationInTest, note: null, url: null, price: 25, quantity: 10};
    }
    if (family === 'reservation') {
        path = '/api/v1/shared-wishlists/' + user.shared.id + '/wishes/' + user.reservedWish + '/reservations/current';
        headers['X-MonKado-Share-Token'] = user.shared.secret;
        if (user.reserved) {
            method = 'DELETE';
            headers['If-Match'] = user.reservationTag;
            expected = [204];
        } else {
            method = 'PUT';
            body = {quantity: 1};
            expected = [201];
        }
    }
    const response = request(user, method, path, body, expected, family, headers);
    const result = response.status === 204 ? null : json(response);
    let valid = expected.includes(response.status);
    if (valid && family === 'lists') valid = Array.isArray(result) && result.length === 5;
    if (valid && family === 'wishes') valid = Array.isArray(result?.wishes) && result.wishes.length === 20;
    if (valid && family === 'shared') valid = Array.isArray(result?.wishes) && result.wishes.length === 20;
    if (valid && family === 'update') {
        valid = result?.id === list.wishes[0] && result.name === body.name;
        user.etag = response.headers.Etag;
    }
    if (valid && family === 'reservation') {
        valid = response.status === 204 || result?.quantity === 1;
        user.reserved = response.status !== 204;
        user.reservationTag = response.headers.Etag;
    }
    outcome.add(valid ? 1 : 0, tags);
    failures.add(!valid, tags);
    status.add(response.status, tags);
    if (valid) duration.add(response.timings.duration, tags);
}
