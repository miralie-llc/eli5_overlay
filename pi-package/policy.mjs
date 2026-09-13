export const forbidden = new Set(['read', 'write', 'edit', 'bash', 'powershell', 'grep', 'find', 'ls']);

export function createPolicy(config) {
  if (config.version !== 1 || !['help', 'session'].includes(config.profile)) throw new Error('Unsupported F1 policy');
  if (!Array.isArray(config.tools) || !Number.isInteger(config.maxToolCalls) || config.maxToolCalls < 1 || config.maxToolCalls > 30)
    throw new Error('Invalid F1 policy');
  const allowed = new Set(config.tools.filter(name => !forbidden.has(name)));
  let calls = 0;
  return {
    enabled(name) { return config.profile === 'session' || allowed.has(name); },
    begin() { calls = 0; },
    check(name) {
      if (config.profile === 'session') return undefined;
      if (!allowed.has(name)) return { block: true, terminate: true, reason: 'This capability is not enabled in Help. Use Session for broader work.' };
      if (++calls > config.maxToolCalls) return { block: true, terminate: true, reason: 'Help reached its tool-call limit. Ask a smaller question.' };
      return undefined;
    }
  };
}
