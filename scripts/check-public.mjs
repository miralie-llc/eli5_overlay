import { execFileSync } from 'node:child_process';
import { readFileSync, statSync } from 'node:fs';
const files = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { encoding: 'utf8' }).split('\0').filter(Boolean);
let failed = false;
const patterns = [new RegExp('-----BEGIN ' + '(?:RSA |EC |OPENSSH )?PRIVATE KEY-----'), new RegExp('\\b' + 'gh[pousr]_[A-Za-z0-9]{30,}'), new RegExp('\\b' + 'sk-(?:proj-)?[A-Za-z0-9_-]{30,}')];
for (const file of files) {
  if (/(^|\/)(private|node_modules)(\/|$)|(^|\/)(auth\.json|\.env)$|\.(pfx|p12)$/i.test(file)) { console.error('Forbidden public path:', file); failed = true; continue; }
  if (statSync(file).size > 2_000_000) continue;
  const data = readFileSync(file, 'utf8');
  if (patterns.some(pattern => pattern.test(data))) { console.error('Potential secret in:', file); failed = true; }
}
if (failed) process.exitCode = 1;
else console.log(`PASS public file check (${files.length} files; full history scanning also runs in CI)`);
