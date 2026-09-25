import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const sourceUrl = new URL('../../src/Operations.Performance/scenarios.js', import.meta.url);
const source = readFileSync(sourceUrl, 'utf8');

async function harness(profile = 'smoke') {
    const points = [];
    const calls = [];
    const clock = {now: 0};
    const control = {override: null};
    const accounts = Array.from({length: 10}, (_, member) => ({
        memberId: 'member-' + member, email: 'member-' + member + '@mk816.invalid',
        lists: Array.from({length: 5}, (_, list) => ({
            id: 'list-' + member + '-' + list,
            wishes: Array.from({length: 20}, (_, wish) => 'wish-' + member + '-' + wish),
        })),
    }));
    const execution = {vu: {idInInstance: 1}, scenario: {iterationInTest: 0, startTime: 0}};
    class Metric {
        constructor(name) { this.name = name; }
        add(value, tags) { points.push({metric: this.name, value, tags}); }
    }
    class CookieJar {
        cookiesForURL() { return {refresh: ['synthetic']}; }
        set(...args) { calls.push({cookie: args}); }
    }
    const http = {
        CookieJar, expectedStatuses: (...statuses) => statuses,
        file: (data, name, type) => ({data, name, type}),
        request(method, url, body, options) {
            calls.push({method, url, options});
            if (control.override) return control.override(method, url, body, options);
            const payload = typeof body === 'string' ? JSON.parse(body) : body;
            let value = {};
            let status = 200;
            if (url.endsWith('/security/csrf-token')) value = {token: 'synthetic-csrf'};
            else if (url.endsWith('/auth/sessions') || url.endsWith('/auth/sessions/refresh')) {
                value = {accessToken: 'synthetic-token', expiresIn: 900};
            } else if (url.endsWith('/share-link')) {
                value = {id: 'share', shareUrl: 'https://mk816.test/#synthetic'};
                status = 201;
            } else if (url.endsWith('/participants')) status = 201;
            else if (url.endsWith('/reservations/current')) {
                status = method === 'DELETE' ? 204 : 201;
                value = {quantity: 1};
            } else if (url.endsWith('/image')) value = {};
            else if (url.endsWith('/wishlists')) value = [1, 2, 3, 4, 5];
            else if (url.endsWith('/wishes') || url.includes('/shared-wishlists/')) {
                value = {wishes: new Array(20)};
            } else if (method === 'PUT') {
                value = {id: url.split('/').at(-1), ...payload};
            }
            return {status, headers: {Etag: '"1"'}, timings: {duration: 12}, json: () => value};
        },
    };
    const context = vm.createContext({
        __ENV: {PROFILE: profile, SHARD: '0'},
        open: path => path.endsWith('.json') ? JSON.stringify({accounts, password: 'synthetic-password'}) : new ArrayBuffer(8),
        Date: class extends Date { static now() { return clock.now; } },
    });
    const dependencies = {
        'k6/http': {default: http}, 'k6/execution': {default: execution},
        'k6': {fail: code => { throw new Error(code); }},
        'k6/metrics': {Counter: Metric, Trend: Metric, Rate: Metric},
    };
    const module = new vm.SourceTextModule(source, {context, identifier: sourceUrl.href});
    await module.link(name => new vm.SyntheticModule(Object.keys(dependencies[name]), function () {
        for (const [key, value] of Object.entries(dependencies[name])) this.setExport(key, value);
    }, {context}));
    await module.evaluate();
    return {api: module.namespace, points, calls, clock, control, execution, http};
}

test('setup uses real session, CSRF, image and sharing contracts without a remote URL', async () => {
    const h = await harness();
    const actors = h.api.setup();
    assert.equal(10, actors.length);
    assert.equal(100, h.calls.filter(call => call.method).length);
    assert.ok(h.calls.filter(call => call.url).every(call => call.url.startsWith('https://mk816.test/')));
    assert.equal(false, h.api.options.insecureSkipTLSVerify);
    assert.equal(0, h.api.options.maxRedirects);
    for (let iteration = 0; iteration < 20; iteration++) {
        h.execution.scenario.iterationInTest = iteration;
        h.api.business(actors);
    }
    assert.equal(20, h.points.filter(point => point.metric === 'business_ok').length);
    assert.ok(h.points.filter(point => point.metric === 'business_ok').every(point => point.value === 1));
    h.clock.now = 850000;
    h.api.business(actors);
    assert.ok(h.calls.some(call => call.url?.endsWith('/auth/sessions/refresh')));
    const before = h.calls.length;
    h.execution.scenario.iterationInTest = 30;
    h.api.business(actors);
    assert.equal(before, h.calls.length);
});

test('profiles retain bounded VUs, exact rates and separate warmup from measurements', async () => {
    for (const profile of ['nominal', 'images', 'exports', 'quotas', 'concurrency', 'stress']) {
        const h = await harness(profile);
        const actors = h.api.setup();
        h.api.business(actors);
        h.execution.scenario.iterationInTest = 180;
        h.api.business(actors);
        assert.equal('warmup', h.points.filter(point => point.metric === 'business_ok')[0].tags.phase);
        assert.equal('measure', h.points.filter(point => point.metric === 'business_ok')[1].tags.phase);
        assert.equal(7, h.api.options.scenarios.traffic.maxVUs);
        assert.equal(profile === 'stress' ? 'ramping-arrival-rate' : 'constant-arrival-rate',
            h.api.options.scenarios.traffic.executor);
        if (profile !== 'stress') assert.ok(h.api.options.thresholds['business_ms{phase:measure,family:lists}']);
        if (profile === 'stress') {
            for (const [iteration, stage] of [[330, '1'], [630, '2'], [1230, '3']]) {
                h.execution.scenario.iterationInTest = iteration;
                h.api.business(actors);
                assert.equal(stage, h.points.filter(point => point.metric === 'business_ok').at(-1).tags.stage);
            }
        }
    }
});

test('unexpected status and invalid successful payloads cannot produce fast passing measurements', async () => {
    const h = await harness();
    const actors = h.api.setup();
    h.api.business(actors);
    for (const iteration of [0, 3, 5, 8, 9]) {
        h.execution.scenario.iterationInTest = iteration;
        h.control.override = () => ({status: 500, headers: {}, timings: {duration: 1}, json: () => null});
        h.api.business(actors);
        assert.equal(0, h.points.filter(point => point.metric === 'business_ok').at(-1).value);
    }
    for (const iteration of [0, 3, 5, 8]) {
        h.execution.scenario.iterationInTest = iteration;
        h.control.override = () => ({status: 200, headers: {}, timings: {duration: 1}, json: () => { throw new Error('bad json'); }});
        h.api.business(actors);
        assert.equal(0, h.points.filter(point => point.metric === 'business_ok').at(-1).value);
    }
    h.control.override = () => ({status: 401});
    assert.throws(() => h.api.setup(), /FIXTURE_OR_SESSION_FAILED/);
    assert.ok(h.points.some(point => point.metric === 'setup_failures' && point.value === 1));
});
