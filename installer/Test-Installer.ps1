$ErrorActionPreference = 'Stop'
$setup = Resolve-Path (Join-Path $PSScriptRoot '..\release\installer\MikroTikManager-Setup.exe')
$app = Join-Path $env:ProgramFiles 'Indigo\MikroTik Manager'
$exe = Join-Path $app 'MikroTikManager.exe'
function Run-Exe($Path,$Arguments) {
    $p = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Process failed: $Path ($($p.ExitCode))" }
}
# Use only the disposable GitHub Windows runner. Never seed a user installation.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run this integration test only on a disposable GitHub Actions runner.' }
$legacyId = [Guid]::NewGuid().ToString('N')
$taskAction = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit 0'
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddDays(30)
Register-ScheduledTask -TaskName $legacyId -TaskPath '\MikroTik Manager\' -Action $taskAction -Trigger $trigger -User 'SYSTEM' | Out-Null
Run-Exe $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $env:TEMP 'MikroTikManager-setup.log') + '"')
if (Get-ScheduledTask -TaskName $legacyId -TaskPath '\MikroTik Manager\' -ErrorAction SilentlyContinue) { throw 'Legacy task was not migrated' }
if (!(Test-Path (Join-Path $env:ProgramData ("MikroTik Manager\MigratedTasks\$legacyId.xml")))) { throw 'Legacy task archive missing' }
if ((Get-Service IndigoMikroTikScheduler).Status -ne 'Running') { throw 'Service not running after Setup' }
& (Join-Path $app 'Service-Control.ps1') -Action Stop
if ($LASTEXITCODE -ne 0) { throw 'Service stop failed' }
Run-Exe $exe '--seed-service-test'
Start-Service IndigoMikroTikScheduler
$passed = $false
for ($i=0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 2
    $p = Start-Process $exe -ArgumentList '--verify-service-test' -Wait -PassThru
    if ($p.ExitCode -eq 0) { $passed = $true; break }
}
if (!$passed) { throw 'SYSTEM service did not process the saved job with the desktop closed' }
$jobsFile = Join-Path $env:ProgramData 'MikroTik Manager\jobs.dat'
$beforeRepair = (Get-FileHash $jobsFile).Hash
# Repeat install exercises stop/reconfigure/start and preservation of encrypted jobs.
Run-Exe $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $env:TEMP 'MikroTikManager-setup.log') + '"')
Run-Exe $exe '--verify-service-test'
if ((Get-FileHash $jobsFile).Hash -ne $beforeRepair) { throw 'Repair/restart changed a finished job' }
Run-Exe (Join-Path $app 'unins000.exe') ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $env:TEMP 'MikroTikManager-setup.log') + '"')
if (Get-Service IndigoMikroTikScheduler -ErrorAction SilentlyContinue) { throw 'Service remains after uninstall' }
if (!(Test-Path (Join-Path $env:ProgramData 'MikroTik Manager\jobs.dat'))) { throw 'Uninstall removed retained data' }
Write-Output 'Installer, SYSTEM scheduler, upgrade/repair and uninstall checks passed. No routers contacted.'
