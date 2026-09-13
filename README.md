# Inspiration

The tool in this repository was made by Astra.  All code and documentation here was written by AI based on a prompt (in a single shot which is quite impressive).  I wanted a simple UI that I could use when I pressed F1 that would connect to a specific pi-agent that I have configured (in this case - a cheap model from OpenRouter).  

The purpose was a fast way to ask very simple "explain like I'm 5" questions and gets short responses.

It has some other half-baked ideas (exposure to tools, extensions, etc.) that aren't really tested.  Feel free to use or just have Astra make your own version.


# F1 — quick help through your local Pi agent

A small Windows tray app. Press **F1**, type a question, and receive a streamed answer. Escape cancels and closes the conversation. F1 runs as your user, not as a Windows service.

**Help** uses selected Pi capabilities with a time/tool budget. **Session** explicitly switches to your broader Pi setup. Neither is an operating-system sandbox. Voice input and automatic learned procedures are not included in this release.

## Requirements and setup

- Windows 10/11 x64. Windows lifecycle/support requirements still apply.
- Node.js 22 or newer and **Pi 0.85.1** (`@earendil-works/pi-coding-agent`). This release checks the exact version; it never updates Pi.
- .NET 10 Desktop Runtime for the framework-dependent build. The self-contained portable ZIP includes the runtime.

If Pi is not installed, install the supported version yourself:

```powershell
npm install -g @earendil-works/pi-coding-agent@0.85.1
pi
```

Configure and authenticate Pi normally. Extract the portable ZIP into a user-writable directory and run `F1.exe`. It starts in the tray without taking focus. Right-click its tray icon for **Settings**.

The default Pi path is the standard Windows npm global installation. For another installation, set **Pi cli.js path** to its `dist/bundle/cli.js` and set **Node executable** as needed. Optionally point **Pi agent directory** at your existing configuration.

Press F1. Type `What is adamantium?` and press Enter. Use Shift+Enter for a newline, Ctrl+L to clear the input, and Escape to cancel and close. Follow-ups remain in memory until you close the overlay. Clicking elsewhere preserves the current conversation. Every fresh invocation starts in Help.

## Capabilities

Help starts with two tools:

- `f1_weather`: current weather and today's forecast from Open-Meteo. Configure latitude/longitude in Settings or provide them in your question. No automatic geolocation. The free endpoint is for **noncommercial use**; commercial/self-hosted endpoints are configurable.
- `f1_reference`: a bounded English Wikipedia lookup with disambiguation and source attribution.

Add existing local extension, skill, or prompt-template paths in Settings. **Inspect selected capabilities / test Pi** loads those selected extensions and lists their tool/command names and sources. Add the desired names to the Help allowlists. This checks RPC and extension loading without making a paid model request. Built-in shell and filesystem tools cannot be enabled in Help.

Skills that require shell or arbitrary file access need Session or a purpose-built restricted tool. Standard Pi selection, confirmation, input, and editor dialogs work; custom terminal UI is not supported by Pi RPC and is incompatible with this overlay.

To reuse a speed-dial extension, add its installed local path and configure a model command such as `/switch fast`. F1 checks that the command exists and that Pi reports a selected model afterward. Your model definitions and authentication stay with Pi. Add `switch` to the Help command list only if you also want to invoke it manually.

Help defaults to 30 seconds and six tool calls per request. It will not install packages or repair your environment to answer a question. When broader access is needed, deliberately choose Session. [Write a capability](docs/capabilities.md).

## Privacy

Foreground executable name and window title are captured once when opening the overlay, displayed above the question, and included with ordinary questions. Disable this in Settings if unwanted. There is no screen capture or continuous monitoring. Your configured model provider receives questions, enabled context, and tool results.

Settings and scratch directories live under `%LOCALAPPDATA%\Miralie\F1`. F1 does not store questions, answers, tool results, or window titles in its diagnostics. Pi is started with `--no-session`; selected third-party extensions can have their own persistence and access behavior. F1 does not copy Pi credentials. Optional weather credentials use Windows Credential Manager. [Security boundaries](SECURITY.md).

## Build, test, and package

Install the .NET SDK selected by `global.json`. No third-party NuGet packages or npm runtime dependencies are required for F1 itself; WPF, Windows Forms (tray icon), and Windows APIs provide the desktop integration. Pi is an external prerequisite.

```powershell
dotnet restore F1.slnx --locked-mode
dotnet build F1.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet tests/F1.Tests/bin/Release/net10.0-windows/F1.Tests.dll
node --test pi-package/test/*.test.mjs
node scripts/pi-smoke.mjs "C:\path\to\pi-coding-agent\dist\bundle\cli.js"
node scripts/backend-smoke.mjs "C:\path\to\pi-coding-agent\dist\bundle\cli.js"
dotnet tests/F1.Tests/bin/Release/net10.0-windows/F1.Tests.dll --render artifacts/overlay.png
.\scripts\package.ps1
```

The C# test executable returns a nonzero exit code on failure and includes real subprocess fixtures. The Pi smoke test uses an empty temporary agent directory, never user authentication or paid model calls. Packaging creates `artifacts/F1-win-x64.zip` with a self-contained runtime. For a smaller package requiring the Desktop Runtime, use `-FrameworkDependent`.

Run a local build with `src\F1.App\bin\Release\net10.0-windows\F1.exe`; add `--open` to open the overlay immediately. Windows CI builds, runs tests, scans source/history, and uploads the portable artifact. It does not publish a GitHub release automatically.

MIT licensed. Service terms and data attribution are separate: see [third-party notices](THIRD-PARTY-NOTICES.md).
