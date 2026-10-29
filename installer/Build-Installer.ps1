$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content (Join-Path $PSScriptRoot '..\MikroTikManager.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (!(Test-Path $iscc)) {
    choco install innosetup --yes --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup installation failed' }
}
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'MikroTikManager.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$setup = Join-Path $PSScriptRoot '..\release\installer\MikroTikManager-Setup.exe'
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  MikroTikManager-Setup.exe" | Set-Content "$setup.sha256.txt"
