import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import assert from 'node:assert/strict';

const root = await mkdtemp(join(tmpdir(), 'f1-backend-smoke-'));
const agent = join(root, 'agent'); await mkdir(agent);
let assertionError, requests = 0;
const server = createServer(async (req, res) => {
  try {
    let body = ''; for await (const chunk of req) body += chunk;
    const data = JSON.parse(body); requests++;
    const last = data.messages.filter(m => m.role === 'user').at(-1).content;
    const text = typeof last === 'string' ? last : JSON.stringify(last);
    assert.ok(!text.includes('must never reach model'));
    if (text.includes('fixture followup')) assert.ok(JSON.stringify(data.messages).includes('fixture hello'));
    if (text.includes('fixture fresh')) assert.ok(!JSON.stringify(data.messages).includes('fixture hello'));
    if (!text.includes('fixture session')) assert.deepEqual(data.tools.map(t => t.function.name).sort(), ['f1_reference', 'f1_weather']);
    else assert.ok(data.tools.some(t => t.function.name === 'bash'));
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    const chunk = (delta, finish_reason = null) => res.write('data: ' + JSON.stringify({ id: 'fixture', object: 'chat.completion.chunk', created: 1, model: 'fixture', choices: [{ index: 0, delta, finish_reason }] }) + '\n\n');
    if (text.includes('fixture budget')) {
      chunk({ role: 'assistant', tool_calls: [{ index: 0, id: 'call_' + requests, type: 'function', function: { name: 'f1_weather', arguments: '{}' } }] });
      chunk({}, 'tool_calls'); res.end('data: [DONE]\n\n'); return;
    }
    chunk({ role: 'assistant', content: 'Fixture ' });
    const timer = setTimeout(() => { chunk({ content: 'answer.' }); chunk({}, 'stop'); res.end('data: [DONE]\n\n'); }, text.includes('fixture slow') ? 5000 : 30);
    res.on('close', () => clearTimeout(timer));
  } catch (error) { assertionError = error; res.writeHead(500); res.end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  await writeFile(join(agent, 'models.json'), JSON.stringify({ providers: { fixture: { baseUrl: `http://127.0.0.1:${server.address().port}/v1`, api: 'openai-completions', apiKey: 'non-secret-local-fixture', models: [{ id: 'fixture', reasoning: false }] } } }));
  await writeFile(join(agent, 'settings.json'), JSON.stringify({ defaultProvider: 'fixture', defaultModel: 'fixture', retry: { enabled: false } }));
  const extension = join(root, 'fixture.ts');
  await writeFile(extension, `export default function(pi) {
    pi.registerCommand('fixture-switch', { description: 'Fixture model selection', handler: async (_, ctx) => { await pi.setModel(ctx.modelRegistry.find('fixture','fixture')); } });
    pi.registerCommand('fixture-confirm', { description: 'Fixture dialog', handler: async (_, ctx) => { await ctx.ui.confirm('Fixture confirmation','Synthetic action only.'); } });
  }`);
  const child = spawn('dotnet', [resolve('tests/F1.Tests/bin/Release/net10.0-windows/F1.Tests.dll'), '--backend-smoke', resolve(process.argv[2]), agent, join(root, 'data'), extension, resolve('pi-package/index.ts')], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = ''; child.stdout.on('data', value => { output += value.toString(); });
  // This process uses entirely synthetic fixtures; print only a fixed outcome.
  child.stderr.on('data', () => {});
  const timer = setTimeout(() => child.kill(), 50000);
  const code = await new Promise(resolve => child.on('exit', resolve)); clearTimeout(timer);
  if (assertionError) throw assertionError;
  assert.equal(code, 0, 'C# Pi integration fixture failed'); assert.ok(requests >= 5);
  console.log(output.trim());
} finally {
  server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
  await rm(root, { recursive: true, force: true });
}
