[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Aprire PowerShell come amministratore sulla VM.'}
$service=Get-CimInstance Win32_Service -Filter "Name='TACommesseServer'"
if(!$service){throw 'TACommesseServer non installato su questa macchina.'}
$exe=$service.PathName.Trim().Trim('"')
if(!(Test-Path -LiteralPath $exe -PathType Leaf) -or [IO.Path]::GetFileName($exe) -ne 'TA.Server.exe'){throw 'Percorso servizio non riconosciuto. Nessuna modifica eseguita.'}
$target=[IO.Path]::GetFullPath((Split-Path -Parent $exe)).TrimEnd('\')
$payload=Join-Path $PSScriptRoot 'Server'
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server-manifest.json') -Raw | ConvertFrom-Json
if(!$manifest -or $manifest.Count -lt 2){throw 'Manifest incompleto.'}
# Ogni percorso viene risolto e verificato prima di arrestare il servizio.
$entries=@(foreach($entry in $manifest){
    $relative=[string]$entry.Path
    if([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($relative -split '[\\/]' | Where-Object {$_ -in @('.','..')})){throw 'Percorso pacchetto non valido.'}
    if([IO.Path]::GetFileName($relative) -in @('appsettings.json','server-settings.json')){throw 'Il pacchetto non deve sovrascrivere impostazioni.'}
    $source=Join-Path $payload $relative
    $destination=[IO.Path]::GetFullPath((Join-Path $target $relative))
    if(!$destination.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Destinazione esterna alla cartella del servizio.'}
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Sha256){throw ('Hash non valido: '+$relative)}
    [pscustomobject]@{Relative=$relative;Source=$source;Destination=$destination;Existed=(Test-Path -LiteralPath $destination)}
})
$backup=Join-Path $env:ProgramData ('Tassinari\CommesseServer\BackupMcp\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$wasRunning=$service.State -eq 'Running'
$touched=[Collections.Generic.List[object]]::new()
try {
    Write-Host 'Arresto servizio e backup dei file da aggiornare...'
    Stop-Service TACommesseServer -ErrorAction Stop
    (Get-Service TACommesseServer).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))
    foreach($entry in $entries){
        if($entry.Existed){
            $old=Join-Path $backup $entry.Relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force | Out-Null
            Copy-Item -LiteralPath $entry.Destination -Destination $old
        }
    }
    $entries | Select-Object Relative,Existed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'journal.json')
    foreach($entry in $entries){
        New-Item -ItemType Directory -Path (Split-Path -Parent $entry.Destination) -Force | Out-Null
        $touched.Add($entry)
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    }
    Start-Service TACommesseServer
    (Get-Service TACommesseServer).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $env:TA_MCP_TRANSPORT='local'
    & (Join-Path $PSScriptRoot 'Mcp\TA.Mcp.exe') --check
    if($LASTEXITCODE -ne 0){throw 'Controllo del canale locale fallito.'}
    if(!$wasRunning){Stop-Service TACommesseServer}
    Write-Host ('Aggiornamento e controllo riusciti. Backup: '+$backup)
} catch {
    $failure=$_
    Write-Warning 'Aggiornamento non riuscito: ripristino dei file precedenti.'
    Stop-Service TACommesseServer -ErrorAction Stop
    (Get-Service TACommesseServer).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))
    foreach($entry in $touched){
        if($entry.Existed){Copy-Item -LiteralPath (Join-Path $backup $entry.Relative) -Destination $entry.Destination -Force}
        elseif(Test-Path -LiteralPath $entry.Destination){Remove-Item -LiteralPath $entry.Destination -Force}
    }
    if($wasRunning){Start-Service TACommesseServer}
    throw $failure
}
