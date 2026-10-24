# Installa, arresta o rimuove solo il servizio di questa cartella applicativa.
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Install','Stop','Remove')][string]$Action)
$ErrorActionPreference='Stop'
$name='TACommesseServer'
$exe=Join-Path $PSScriptRoot 'TA.Server.exe'
$service=Get-CimInstance Win32_Service -Filter "Name='$name'"
if ($service -and $service.PathName.Trim('"') -ne $exe) { throw 'Il nome del servizio appartiene a un eseguibile diverso. Nessuna modifica eseguita.' }
if ($service -and $service.State -ne 'Stopped') {
    Stop-Service -Name $name -ErrorAction Stop
    (Get-Service $name).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))
}
if ($Action -eq 'Remove') {
    if ($service) { & sc.exe delete $name; if ($LASTEXITCODE -ne 0) { throw 'Rimozione servizio non riuscita' } }
    # Le impostazioni ProgramData e i JSON condivisi non vengono eliminati.
    exit 0
}
if ($Action -eq 'Stop') { exit 0 }
if (!$service) {
    & sc.exe create $name binPath= ('"'+$exe+'"') start= delayed-auto obj= 'NT AUTHORITY\NetworkService' DisplayName= 'TA Gestione Commesse Server'
    if ($LASTEXITCODE -ne 0) { throw 'Creazione servizio non riuscita' }
}
# Abilita un SID stabile per dare scrittura solo ai registri, anche cambiando account AD.
& sc.exe sidtype $name unrestricted
if ($LASTEXITCODE -ne 0) { throw 'Configurazione SID servizio non riuscita' }
$logs = Join-Path $env:ProgramData 'Tassinari\CommesseServer\Logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
 $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
}
$serviceSid = ([System.Security.Principal.NTAccount]::new('NT SERVICE\TACommesseServer')).Translate([System.Security.Principal.SecurityIdentifier])
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($serviceSid,'Modify','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $logs -AclObject $acl
if (![System.Diagnostics.EventLog]::SourceExists('TA.Server')) { New-EventLog -LogName Application -Source 'TA.Server' }
# Un nuovo servizio resta fermo finché gruppo e SPN non sono configurati.
$config=Join-Path $env:ProgramData 'Tassinari\CommesseServer\server-settings.json'
if (Test-Path -LiteralPath $config) {
    $settings=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    if ($settings.Server.AllowedGroup) {
        Start-Service $name
        (Get-Service $name).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    }
}
