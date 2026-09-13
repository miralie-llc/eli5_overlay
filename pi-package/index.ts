import type { ExtensionAPI } from '@earendil-works/pi-coding-agent';
import { createPolicy } from './policy.mjs';
import { weatherTool, referenceTool } from './starter.mjs';

export default function (pi: ExtensionAPI) {
  // Fail closed when explicitly launched by F1 with an invalid policy.
  // A standalone Pi installation of the starter pack gets no built-in-tool policy.
  const config = process.env.F1_POLICY ? JSON.parse(process.env.F1_POLICY) : { version: 1, profile: 'session', tools: [], maxToolCalls: 6 };
  const policy = createPolicy(config);
  pi.registerTool(weatherTool(config, fetch, process.env.F1_WEATHER_KEY ?? ''));
  pi.registerTool(referenceTool());
  const apply = () => {
    if (config.profile === 'help') pi.setActiveTools(pi.getAllTools().filter(t => policy.enabled(t.name)).map(t => t.name));
  };
  pi.on('session_start', async () => { apply(); });
  pi.on('before_agent_start', async () => { apply(); });
  pi.on('tool_call', async (event, ctx) => {
    const result = policy.check(event.toolName);
    if (result) ctx.ui.notify('F1_POLICY_V1:' + (policy.enabled(event.toolName) ? 'limit' : 'unavailable'), 'warning');
    return result;
  });
  pi.registerCommand('f1-control', {
    description: 'F1 internal protocol v1 (not a user command)',
    handler: async (args, ctx) => {
      if (!['inspect', 'begin'].includes(args.trim())) throw new Error('Unknown F1 control action');
      if (args.trim() === 'begin') policy.begin();
      apply();
      ctx.ui.notify('F1_CONTROL_V1:' + JSON.stringify({ version: 1, tools: pi.getAllTools().map(t => ({
        name: t.name, description: t.description, source: t.sourceInfo?.path ?? 'extension', enabled: policy.enabled(t.name)
      })) }), 'info');
    }
  });
}
