import test from 'node:test';
import assert from 'node:assert/strict';
import { fetchJson, weatherTool, referenceTool } from '../starter.mjs';
import { createPolicy } from '../policy.mjs';

const payload = result => JSON.parse(result.content[0].text);
const response = data => new Response(JSON.stringify(data), { headers: { 'content-type': 'application/json' } });
const help = () => createPolicy({ version: 1, profile: 'help', tools: ['f1_weather', 'bash'], maxToolCalls: 2 });
test('policy blocks built-ins, unknown and late-registered tools, and enforces a per-request budget', () => {
  const policy = help();
  assert.equal(policy.enabled('bash'), false);
  assert.equal(policy.check('bash').block, true);
  assert.equal(policy.check('late_tool').block, true);
  assert.equal(policy.check('f1_weather'), undefined);
  assert.equal(policy.check('f1_weather'), undefined);
  assert.equal(policy.check('f1_weather').terminate, true);
  policy.begin(); assert.equal(policy.check('f1_weather'), undefined);
});
test('Session permits broader capabilities while invalid policy fails closed', () => {
  assert.equal(createPolicy({ version: 1, profile: 'session', tools: [], maxToolCalls: 6 }).check('bash'), undefined);
  assert.throws(() => createPolicy({ version: 2 }));
});
test('weather asks for missing location without making a request', async () => {
  const tool = weatherTool({}, () => { throw new Error('Must not call'); });
  assert.match(payload(await tool.execute('1', {})).error, /location/);
});
test('weather performs structured fetch, preserves units and excludes credentials from result', async () => {
  const tool = weatherTool({ latitude: 12, longitude: 34, locationLabel: 'Example' }, async (url, options) => {
    assert.equal(url.searchParams.get('apikey'), 'fixture-key');
    assert.equal(options.redirect, 'error');
    return response({ current: { temperature_2m: 21 }, current_units: { temperature_2m: '°C' }, daily: { temperature_2m_max: [25] } });
  }, 'fixture-key');
  const result = payload(await tool.execute('1', {}));
  assert.equal(result.current.temperature_2m, 21); assert.equal(result.current_units.temperature_2m, '°C');
  assert.match(result.attribution, /Open-Meteo/); assert.ok(!JSON.stringify(result).includes('fixture-key'));
});
test('weather sanitizes failed requests and malformed provider data', async () => {
  for (const fetcher of [async () => { throw new Error('https://example.invalid/?apikey=fixture-key'); }, async () => response({})]) {
    const result = payload(await weatherTool({ latitude: 1, longitude: 2 }, fetcher, 'fixture-key').execute('1', {}));
    assert.match(result.error, /failed/); assert.ok(!JSON.stringify(result).includes('fixture-key'));
  }
});
test('HTTP reader rejects oversized, malformed, and failed responses', async () => {
  await assert.rejects(fetchJson('https://example.invalid', undefined, async () => new Response('x'.repeat(300_000))), /size limit/);
  await assert.rejects(fetchJson('https://example.invalid', undefined, async () => new Response('{')), /invalid data/);
  await assert.rejects(fetchJson('https://example.invalid', undefined, async () => new Response('', { status: 503 })), /unavailable/);
});
test('HTTP request carries cancellation and timeout', async () => {
  const controller = new AbortController(); controller.abort();
  await assert.rejects(fetchJson('https://example.invalid', controller.signal, async (_url, options) => { options.signal.throwIfAborted(); }));
});
test('reference returns ambiguity candidates instead of inventing the topic', async () => {
  const tool = referenceTool(async () => response({ query: { search: [{ title: 'Sebastian (name)' }, { title: 'Sebastian (game)' }] } }));
  const result = payload(await tool.execute('1', { query: 'Sebastian' }));
  assert.equal(result.candidates.length, 2);
});
test('reference exact match returns bounded text and attribution', async () => {
  let calls = 0;
  const tool = referenceTool(async () => ++calls === 1 ? response({ query: { search: [{ title: 'Adamantium' }] } }) : response({ query: { pages: [{ title: 'Adamantium', extract: 'A fictional metal. '.repeat(200) }] } }));
  const result = payload(await tool.execute('1', { query: 'adamantium' }));
  assert.equal(calls, 2); assert.ok(result.excerpt.length <= 2400);
  assert.equal(result.source, 'https://en.wikipedia.org/wiki/Adamantium'); assert.match(result.attribution, /CC BY-SA/);
});
test('reference failures and missing articles are recoverable', async () => {
  const result = payload(await referenceTool(async () => response({ query: { pages: [{ missing: true }] } })).execute('1', { query: 'missing', title: 'missing' }));
  assert.match(result.error, /unavailable/);
});
