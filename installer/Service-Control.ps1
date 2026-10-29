param([ValidateSet('Install','Stop','Remove')][string]$Action)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $PSScriptRoot 'setup-service.log') -Append | Out-Null
$name = 'IndigoMikroTikScheduler'
$exe = Join-Path $PSScriptRoot 'MikroTikManager.exe'
function Invoke-Sc([string[]]$Arguments) {
    & "$env:SystemRoot\System32\sc.exe" @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Service configuration failed ($LASTEXITCODE)." }
}
function Stop-Scheduler {
    $service = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        $service.Stop()
        $service.WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2))
    }
}
try {
    if ($Action -eq 'Stop') { Stop-Scheduler; exit 0 }
    if ($Action -eq 'Remove') {
        Stop-Scheduler
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) { Invoke-Sc @('delete',$name) }
        exit 0
    }
    $prepare = Start-Process -FilePath $exe -ArgumentList '--prepare-install' -Wait -PassThru
    if ($prepare.ExitCode -ne 0) { throw 'Could not protect shared scheduler data.' }
    Stop-Scheduler
    $binaryPath = '"' + $exe + '" --service'
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        $existing = Get-CimInstance Win32_Service -Filter "Name='$name'"
        $changed = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{ PathName=$binaryPath; StartMode='Automatic'; StartName='LocalSystem' }
        if ($changed.ReturnValue -ne 0) { throw "Service repair failed ($($changed.ReturnValue))." }
    } else {
        New-Service -Name $name -BinaryPathName $binaryPath -DisplayName 'Indigo MikroTik Scheduler' -StartupType Automatic | Out-Null
    }
    Invoke-Sc @('config',$name,'start=','delayed-auto')
    Invoke-Sc @('description',$name,'Runs MikroTik Manager schedules when the desktop is closed. Keep this PC powered on.')
    Invoke-Sc @('failure',$name,'reset=','86400','actions=','restart/60000/restart/60000/none/0')
    Start-Service -Name $name
    (Get-Service $name).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    # Preserve exact old task definitions before moving ownership to the service.
    $archive = Join-Path $env:ProgramData 'MikroTik Manager\MigratedTasks'
    Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskPath -eq '\MikroTik Manager\' -and $_.TaskName -match '^[0-9a-fA-F]{32}$' } | ForEach-Object {
        New-Item -Path $archive -ItemType Directory -Force | Out-Null
        Export-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath | Set-Content (Join-Path $archive ($_.TaskName + '.xml'))
        Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -Confirm:$false
    }
    exit 0
} catch {
    $_ | Out-String | Write-Error -ErrorAction Continue
    exit 1
}
