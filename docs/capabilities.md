# Extending F1 through Pi

F1 uses the installed Pi CLI through RPC. It does not define another plugin registry. Capabilities may combine Pi tools, skills, prompt templates, and extension commands. A description or skill alone cannot grant a tool permission in Help.

## Add an existing capability

1. Install/configure it using your normal Pi workflow outside F1.
2. Add its absolute local extension path in F1 Settings. Loading it executes trusted code.
3. Inspect the selected resources. The list shows tool/command names, sources, and enabled state.
4. Add the desired tool names or slash-command names to Help's lists. Save settings.

F1 also accepts explicit skill and prompt-template paths. Allow their commands by name (`skill:name` for skills) if you want to invoke them directly. Skills that instruct Pi to run shell commands cannot work in Help without an appropriate restricted tool. Use Session for broad agent tasks.

For a local game reference, start with this extension, saved outside the F1 checkout in your own Pi resource directory:

```typescript
import type { ExtensionAPI } from '@earendil-works/pi-coding-agent';

// Replace this invented fixture with data you own or can redistribute.
const gifts = new Map([['sample character', ['Tea', 'Blue flowers']]]);

export default function (pi: ExtensionAPI) {
  pi.registerTool({
    name: 'my_game_gifts',
    label: 'Game gift reference',
    description: 'Look up gifts in my configured game reference. Ask for the character when missing.',
    parameters: {
      type: 'object',
      properties: { character: { type: 'string' } },
      required: ['character'], additionalProperties: false,
    },
    async execute(_id, params) {
      const found = gifts.get(params.character.toLowerCase());
      return {
        content: [{ type: 'text', text: JSON.stringify({ gifts: found ?? [], source: 'My local reference' }) }],
        details: {},
      };
    },
  });
}
```

Select that extension and enable `my_game_gifts`. No C# changes are needed. A personal agenda capability follows the same pattern: call your existing integration, return a bounded result and its date/timezone, honor cancellation, and keep authentication outside source and settings. Do not label arbitrary code as safe merely because the operation sounds read-only.

## F1's bundled package

`pi-package/index.ts` registers weather/reference tools and the Help policy. `policy.mjs` and `starter.mjs` are dependency-free modules tested with Node's built-in test runner. Pi loads the TypeScript entry; its SDK type import is removed at runtime. The package is bundled locally with the app and can also be installed through normal Pi local-package support.

The internal `/f1-control` command exchanges version-1 tool metadata using a prefixed Pi notification. User input cannot invoke reserved `f1-*` commands. Help checks every model tool call, including dynamically registered tools. An approved extension command still runs trusted extension code; command allowlisting is therefore a separate decision from tool allowlisting.

F1 implements standard RPC extension dialogs. Extensions using `ctx.ui.custom()` must supply their own RPC-compatible path or be used in Pi's terminal UI; F1 cannot detect or emulate all terminal-only behavior.

Weather endpoints must be HTTPS without credentials or query parameters in the URL. Configure an optional Windows Credential Manager key through Settings for a commercial endpoint. The built-in reference tool contacts English Wikipedia only. Neither tool uses curl, a browser, package installation, or a general shell.
