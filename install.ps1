dotnet restore F1.slnx --locked-mode
dotnet build F1.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet tests/F1.Tests/bin/Release/net10.0-windows/F1.Tests.dll
node --test pi-package/test/*.test.mjs
node scripts/pi-smoke.mjs "C:\path\to\pi-coding-agent\dist\bundle\cli.js"
node scripts/backend-smoke.mjs "C:\path\to\pi-coding-agent\dist\bundle\cli.js"
dotnet tests/F1.Tests/bin/Release/net10.0-windows/F1.Tests.dll --render artifacts/overlay.png
.\scripts\package.ps1
