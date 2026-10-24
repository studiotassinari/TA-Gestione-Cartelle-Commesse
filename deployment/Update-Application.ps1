# Eseguito solo all'apertura dell'interfaccia o dal comando manuale. Nessun task pianificato.
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Client','Server')][string]$Component,[switch]$CheckOnly,[switch]$Interactive,[switch]$OnOpen,[int]$ParentId)
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root=$PSScriptRoot
$exe=if ($Component -eq 'Server') { Join-Path $root 'TA.Server.exe' } else { Join-Path $root 'TA-Gestione Cartelle Commesse.exe' }
$cache=if ($Component -eq 'Server') { Join-Path $root 'UpdateCache' } else { Join-Path $env:LOCALAPPDATA 'Tassinari\CommesseClient\Updates' }
function Inform([string]$message) {
 Write-Output $message
 if (Test-Path -LiteralPath $cache) { Add-Content -LiteralPath (Join-Path $cache 'update.log') -Value ((Get-Date -Format o)+' '+$message) -Encoding UTF8 }
 if ($Interactive) { Add-Type -AssemblyName System.Windows.Forms; [void][Windows.Forms.MessageBox]::Show($message,'Aggiornamenti TA') }
}
$lease=$null
$closed=$false
$interface=if ($Component -eq 'Server') { Join-Path $root 'Manager\TA.ServiceManager.exe' } else { $exe }
function Reopen {
 $env:TA_SKIP_UPDATE_ONCE='1'
 Start-Process -FilePath $interface -WindowStyle Normal | Out-Null
 Remove-Item Env:TA_SKIP_UPDATE_ONCE
}
try {
 if (!(Test-Path -LiteralPath $exe)) { throw 'Eseguibile installato non trovato.' }
 $current=[Version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion
 $headers=@{'User-Agent'='TA-Commesse-Updater';Accept='application/vnd.github+json'}
 $release=Invoke-RestMethod -Uri 'https://api.github.com/repos/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/latest' -Headers $headers -TimeoutSec 30
 if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v([0-9]+\.[0-9]+\.[0-9]+)$') { throw 'Release stabile non valida.' }
 $version=$Matches[1]; $latest=[Version]($version+'.0')
 if ($latest -le $current) { Inform ('Versione '+$current.ToString(3)+' aggiornata.'); exit 0 }
 if ($latest.Major -ne $current.Major) { Inform ('Disponibile '+$version+': cambio versione principale, installazione manuale necessaria.'); exit 0 }
 if ($CheckOnly) { Inform ('Disponibile versione '+$version+". Aggiornamento eseguito all'apertura dell'interfaccia."); exit 0 }
 if (!$OnOpen -or $ParentId -le 0) { throw 'Aggiornamento consentito solo da una interfaccia aperta.' }
 New-Item -ItemType Directory -Path $cache -Force | Out-Null
 # Lock locale: evita aggiornamenti contemporanei da task e chiusura applicazione.
 try { $lease=[IO.File]::Open((Join-Path $cache 'update.lock'),'OpenOrCreate','ReadWrite','None') }
 catch [IO.IOException] { exit 0 }
 $base=if ($Component -eq 'Server') { 'TA-Gestione-Cartelle-Commesse-Server-Setup-' } else { 'TA-Gestione-Cartelle-Commesse-Setup-' }
 $name=$base+$version+'.exe'
 $assets=@($release.assets | Where-Object name -eq $name)
 if ($assets.Count -ne 1) { throw 'Installatore non trovato nella release.' }
 $asset=$assets[0]
 $url='https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v'+$version+'/'+$name
 if ($asset.browser_download_url -cne $url -or $asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw 'Origine o impronta GitHub del pacchetto non valida.' }
 $expected=$Matches[1]
 $setup=Join-Path $cache $name
 Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $setup -TimeoutSec 600
 if ((Get-Item $setup).Length -ne $asset.size -or (Get-FileHash $setup -Algorithm SHA256).Hash -ine $expected) { throw 'Verifica integrita installatore fallita; installazione annullata.' }
 $parent=Get-Process -Id $ParentId -ErrorAction Stop
 if ($parent.Path -ine $interface) { throw 'Processo chiamante non corrispondente alla interfaccia installata.' }
 $marker=Join-Path $cache ('exit-'+$ParentId)
 Set-Content -LiteralPath $marker -Value 'update' -Encoding ascii
 # Una finestra nascosta nell'area di notifica non ha MainWindowHandle: la si ripristina tramite il client prima del comando manuale.
 if (!$parent.CloseMainWindow()) { Remove-Item -LiteralPath $marker -ErrorAction SilentlyContinue; throw 'Impossibile chiudere la finestra: riaprirla e riprovare.' }
 if (!$parent.WaitForExit(30000)) { Remove-Item -LiteralPath $marker -ErrorAction SilentlyContinue; throw 'Interfaccia ancora occupata; aggiornamento rinviato senza terminazione forzata.' }
 $closed=$true
 # Non chiude forzatamente applicazioni aperte e non riavvia Windows.
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOCLOSEAPPLICATIONS','/NORESTARTAPPLICATIONS',('/DIR="'+$root+'"'),('/LOG="'+(Join-Path $cache 'installer.log')+'"'))
 if ($Component -eq 'Client') { $arguments += '/CURRENTUSER' }
 $process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
 if ($process.ExitCode -notin @(0,3010)) { throw ('Installatore terminato con codice '+$process.ExitCode+'. Consultare installer.log.') }
 $installed=[Version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion
 if ($installed -lt $latest) { throw 'La versione installata non corrisponde al pacchetto.' }
 Inform ('Aggiornamento a '+$version+' completato. Nessun riavvio automatico del PC.')
 Reopen
 $closed=$false
} catch {
 Inform ('Aggiornamento non completato: '+$_.Exception.Message)
 if ($closed) { Reopen }
 exit 1
} finally { if ($lease) { $lease.Dispose() } }
