param([ValidateSet('Install','Stop','Remove')][string]$Action)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $PSScriptRoot 'setup-service.log') -Append | Out-Null
$name = 'IndigoMikroTikScheduler'
$exe = Join-Path $PSScriptRoot 'MikroTikManager.exe'
function Invoke-Sc([string[]]$Arguments) {
    & "$env:SystemRoot\System32\sc.exe" @Arguments | Out-Null
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
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Invoke-Sc @('config',$name,'binPath=',('"' + $exe + '" --service'),'start=','delayed-auto','obj=','LocalSystem')
    } else {
        Invoke-Sc @('create',$name,'binPath=',('"' + $exe + '" --service'),'start=','delayed-auto','obj=','LocalSystem','DisplayName=','Indigo MikroTik Scheduler')
    }
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
