// No provider requests, user credentials, or persistent sessions are used.
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import assert from 'node:assert/strict';

const entry = process.argv[2];
if (!entry) throw new Error('Usage: node scripts/pi-smoke.mjs <installed Pi dist/bundle/cli.js>');
const root = await mkdtemp(join(tmpdir(), 'f1-pi-smoke-'));
const agent = join(root, 'agent'); const scratch = join(root, 'scratch');
await mkdir(agent); await mkdir(scratch);
const child = spawn(process.execPath, [resolve(entry), '--mode', 'rpc', '--no-session', '--no-context-files', '--no-extensions', '--no-skills', '--no-prompt-templates', '--no-themes', '--no-builtin-tools', '-e', resolve('pi-package/index.ts')], {
  cwd: scratch, windowsHide: true,
  env: { ...process.env, PI_CODING_AGENT_DIR: agent, PI_CODING_AGENT_SESSION_DIR: '', F1_POLICY: JSON.stringify({ version: 1, profile: 'help', tools: ['f1_weather', 'f1_reference'], maxToolCalls: 6 }), F1_WEATHER_KEY: '' },
  stdio: ['pipe', 'pipe', 'pipe']
});
const pending = new Map(); let sequence = 0, buffer = '', metadata;
child.stderr.on('data', () => {}); // Deliberately do not print raw extension output.
child.stdout.setEncoding('utf8');
child.stdout.on('data', chunk => {
  buffer += chunk;
  let index;
  while ((index = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, index); buffer = buffer.slice(index + 1); if (!line.trim()) continue;
    const event = JSON.parse(line);
    if (event.type === 'response') pending.get(event.id)?.(event);
    if (event.type === 'extension_ui_request' && event.method === 'notify' && event.message.startsWith('F1_CONTROL_V1:')) metadata = JSON.parse(event.message.slice(14));
    if (event.type === 'extension_ui_request' && ['select', 'confirm', 'input', 'editor'].includes(event.method))
      child.stdin.write(JSON.stringify({ type: 'extension_ui_response', id: event.id, cancelled: true }) + '\n');
  }
});
async function command(type, data = {}) {
  const id = String(++sequence);
  let timer;
  try {
    return await Promise.race([
      new Promise(resolve => { pending.set(id, resolve); child.stdin.write(JSON.stringify({ id, type, ...data }) + '\n'); }),
      new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Pi smoke command timed out')), 15000); })
    ]);
  } finally { clearTimeout(timer); pending.delete(id); }
}
try {
  const inspected = await command('prompt', { message: '/f1-control inspect' });
  assert.equal(inspected.success, true); assert.equal(metadata.version, 1);
  assert.deepEqual(metadata.tools.filter(t => t.enabled).map(t => t.name).sort(), ['f1_reference', 'f1_weather']);
  const reset = await command('new_session'); assert.equal(reset.success, true); assert.equal(reset.data.cancelled, false);
  await command('prompt', { message: '/f1-control inspect' });
  const state = await command('get_state'); assert.equal(state.data.isStreaming, false);
  assert.ok(!state.data.sessionFile, 'Ephemeral reset must not create a session file');
  await command('clear_queue'); await command('abort');
  assert.ok(!(await readdir(agent)).includes('sessions'), 'No session directory should be persisted');
  console.log('PASS actual Pi 0.85.1: extension load, capability policy, RPC, reset, cancellation, ephemeral sessions');
} finally {
  child.kill(); await new Promise(resolve => child.once('exit', resolve));
  await rm(root, { recursive: true, force: true });
}
