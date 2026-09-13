param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot 'artifacts'
$outputPath = Join-Path $artifactRoot ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish (Join-Path $repoRoot 'src/F1.App/F1.App.csproj') -c Release -r win-x64 --self-contained $selfContained -o $outputPath -m:1 -p:RestoreLockedMode=true -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($document in @('README.md', 'LICENSE', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $outputPath
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $outputPath -Recurse
$forbidden = Get-ChildItem -LiteralPath $outputPath -Recurse -Force | Where-Object { $_.Name -in @('private', '.git', 'auth.json', 'settings.json', '.env', 'node_modules') -or $_.Extension -in @('.pdb', '.pfx', '.p12') }
if ($forbidden) { throw 'Package includes a forbidden file or directory.' }
$zipPath = Join-Path $artifactRoot 'F1-win-x64.zip'
Compress-Archive -Path (Join-Path $outputPath '*') -DestinationPath $zipPath -Force
Write-Output "Portable package: $zipPath"
