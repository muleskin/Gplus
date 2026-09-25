# Installs or updates the Gplus service from the single-file build.
#   1. dotnet publish src/Gplus -p:PublishProfile=SingleExe
#   2. powershell -ExecutionPolicy Bypass -File tools\deploy-service.ps1   (elevated)
# Stops the service, copies publish\Gplus.exe to $Dest, points the service at it, and starts it.
# A transcript is written to %ProgramData%\Gplus\logs\deploy.log.
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\publish\Gplus.exe'),
    [string]$Dest   = 'C:\Program Files\Gplus'
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:ProgramData 'Gplus\logs\deploy.log'
Start-Transcript -Path $log -Append | Out-Null
try {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated prompt.' }

    $Source = (Resolve-Path $Source).Path
    $target = Join-Path $Dest 'Gplus.exe'
    New-Item -ItemType Directory -Force $Dest | Out-Null

    $svc = Get-Service Gplus -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Write-Host 'Stopping Gplus service...'
        Stop-Service Gplus -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    # The exe stays locked for a moment after the service reports Stopped.
    for ($i = 0; $i -lt 30 -and (Get-Process Gplus -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }

    Copy-Item $Source $target -Force
    Write-Host "Copied $Source -> $target"

    if ($svc) {
        # Written directly: PowerShell 5.1 strips embedded quotes when passing them to sc.exe, and an
        # unquoted path with spaces is the "unquoted service path" hijack (C:\Program.exe).
        Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\Gplus' -Name ImagePath -Value "`"$target`""
        Write-Host "Service path set to `"$target`""
        Start-Service Gplus
        (Get-Service Gplus).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
    } else {
        & $target install | Write-Host
    }

    $path = (Get-CimInstance Win32_Service -Filter "Name='Gplus'").PathName
    if ($path -notmatch '^".+"$') { throw "Service path is not quoted: $path" }
    Write-Host "Service Gplus: $((Get-Service Gplus).Status), binary $path"
    Write-Host 'DEPLOY OK'
}
catch {
    Write-Host "DEPLOY FAILED: $_"
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
