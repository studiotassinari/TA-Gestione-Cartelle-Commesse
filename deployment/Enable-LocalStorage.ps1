# Eseguire come amministratore sulla VM dopo l'aggiornamento, con servizio fermo.
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$DatabaseFolder,
 [Parameter(Mandatory=$true)][string]$BackupFolder,
 [string]$CollaboratorsGroup='GG_TA_Commesse_Users'
)
$ErrorActionPreference='Stop'
$settingsPath=Join-Path $env:ProgramData 'Tassinari\CommesseServer\server-settings.json'
$service=Get-CimInstance Win32_Service -Filter "Name='TACommesseServer'"
if($service.State -ne 'Stopped'){throw 'Fermare il servizio prima di cambiare archiviazione.'}
$installed=Get-Item 'C:\Program Files\Tassinari\Commesse Server\TA.Server.exe'
if([version]$installed.VersionInfo.FileVersion -lt [version]'2.23.0.0'){throw 'Installare prima il server 2.23.0 o successivo.'}
$config=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if($config.Server.LocalStorageOnly){throw 'Modalità locale già attiva: non ripetere la transizione.'}
$target=[IO.Path]::GetFullPath($DatabaseFolder).TrimEnd('\')
if($target.StartsWith('\\') -or (Get-Item ([IO.Path]::GetPathRoot($target))).PSDrive.DisplayRoot){throw 'Il database richiede un disco locale.'}
if(!$BackupFolder.StartsWith('\\')){throw 'Specificare una cartella UNC separata per i backup.'}
$old=[IO.Path]::GetFullPath($config.Server.DatabaseFolder).TrimEnd('\')
if($target -eq $old -or $target.StartsWith($old+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Usare una nuova cartella separata dal database precedente.'}
if(Test-Path -LiteralPath (Join-Path $old 'gestionale.sqlite')){throw 'Database gestionale già esistente: preservarlo e trasferirlo prima della transizione.'}
if(!(Test-Path -LiteralPath (Join-Path $old 'inventario-file\inventario.sqlite'))){throw 'Database precedente non trovato.'}
if((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -Force).Count){throw 'La nuova cartella deve essere vuota.'}
foreach($source in @($config.Server.JobsRoot,$config.Server.BidsRoot,$config.Server.ArchivedJobsRoot)){
 if($target.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $target -eq $source){throw 'Destinazione dentro una sorgente originale.'}
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
$acl=Get-Acl -LiteralPath $target
$account=[Security.Principal.NTAccount]::new($service.StartName)
$rule=[Security.AccessControl.FileSystemAccessRule]::new($account,'Modify','ContainerInherit,ObjectInherit','None','Allow')
$acl.AddAccessRule($rule);Set-Acl -LiteralPath $target -AclObject $acl
Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $target 'server-settings-precedenti.json')
$values=@{LocalStorageOnly=$true;DatabaseFolder=$target;ArchiveFolder=(Join-Path $target 'Runtime');BackupFolder=$BackupFolder;LegacyDatabaseFolder=$old;LegacyCatalogFolder=$config.Server.ArchiveFolder;CollaboratorsGroup=$CollaboratorsGroup}
foreach($entry in $values.GetEnumerator()){$config.Server | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force}
$temp=$settingsPath+'.new'
[IO.File]::WriteAllText($temp,($config | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temp -Destination $settingsPath -Force
Write-Output 'Configurazione salvata. Avviare il servizio: preservazione cataloghi e backup precedono la nuova scansione. Nessuna sorgente eliminata.'
