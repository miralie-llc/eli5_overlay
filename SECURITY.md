# Security and privacy

F1's Help profile restricts the tools exposed to the model and checks each tool call. It is **not a sandbox**. Pi and loaded extensions execute as the current Windows user. An extension's own JavaScript, command handlers, initialization, or background work can read files, access credentials, write data, and use the network independently of model tool calls. Enable only extensions you trust. Session deliberately uses your broader Pi ecosystem.

Help disables implicit extension/skill/prompt/context discovery and accepts only explicitly selected installed local resources. Its starter tools use fixed reference hosts or the explicitly configured weather endpoint, bounded HTTPS responses, cancellation, and no shell execution. Model-generated text, foreground window titles, and fetched content are untrusted data. Prompt wording is not a security boundary.

F1 does not install or update Pi or capability packages. A selected extension may have its own lifecycle behavior; review it before loading. Pi's custom terminal UI is not available through RPC. Do not depend on a terminal-only approval extension in the overlay.

## Data and credentials

- Settings: `%LOCALAPPDATA%\Miralie\F1\settings.json`; non-secret configuration only.
- Scratch: `%LOCALAPPDATA%\Miralie\F1\scratch`; a working directory, not access isolation. Session tools can create files there and elsewhere. F1 never recursively clears this directory because it can contain user-created work.
- Diagnostics: bounded `diagnostics.log` containing fixed categories, UTC timestamps, and numeric durations. No raw exceptions, process streams, prompts, answers, tool data, or foreground titles.
- Conversations: Pi `--no-session`, including resets. This disables Pi session-file persistence, not independent extension persistence or provider-side storage.
- Pi credentials: remain in Pi's own store and under its own protection rules. F1 neither copies nor strengthens that store.
- F1 weather keys: Windows Credential Manager, generic targets prefixed `F1/`. Settings contain only the target name. The selected key is passed to the Pi child environment and used for the configured weather service; trusted loaded extensions can read that environment. Remove unused keys through Windows Credential Manager.

Never put secrets in source, examples, command arguments, diagnostic messages, or Git-ignored project metadata. A `.gitignore` rule is not encryption and cannot remove a secret from existing Git history. Rotate any accidentally disclosed credential.

Foreground context is captured only when opening F1 and is visible before submitting. Turn it off in Settings to exclude app names and window titles. Ordinary questions and results go to the provider configured in Pi; weather and reference tools contact their respective services. No telemetry, analytics, automatic location lookup, or screenshot capture is implemented.

## Reporting

Use the repository's GitHub private vulnerability reporting feature when enabled. Do not post credentials, personal prompts, or sensitive reproduction data in public issues. For a local integration, first reproduce with an empty Pi configuration and a synthetic fixture.
