# Compatibilita con interfacce precedenti: avvia esclusivamente l'aggiornatore diretto.
param([ValidateSet('Client','Server')][string]$Component='Client',[switch]$Interactive,[switch]$OnOpen,[switch]$CheckOnly,[int]$ParentId)
$ErrorActionPreference='Stop'
if($CheckOnly){Write-Output 'Usare TA.Updater.exe per la verifica e l aggiornamento diretto.';exit}
$cache=if($Component -eq 'Server'){Join-Path $PSScriptRoot 'UpdateCache'}else{Join-Path $env:LOCALAPPDATA 'Tassinari\CommesseClient\Updates'}
$runner=Join-Path $cache ('runner-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runner -Force|Out-Null
$exe=Join-Path $runner 'TA.Updater.exe'
Copy-Item (Join-Path $PSScriptRoot 'Updater\TA.Updater.exe') $exe
$arguments='--component '+$Component+' --root "'+$PSScriptRoot+'"'
if(!$Interactive){$arguments+=' --automatic'}
Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden