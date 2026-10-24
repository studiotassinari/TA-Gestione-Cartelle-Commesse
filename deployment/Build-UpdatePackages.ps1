[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version,[string]$Output='artifacts/installer')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $Output -Force | Out-Null
foreach($component in @('Client','Server')) {
 $files=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
 $publish=if($component -eq 'Client'){'artifacts/publish'}else{'artifacts/server'}
 $base=(Resolve-Path $publish).Path
 foreach($file in Get-ChildItem -LiteralPath $base -Recurse -File) {
  $relative=$file.FullName.Substring($base.Length+1).Replace('\','/')
  if($file.Name -in @('appsettings.json','server-settings.json') -or $file.Extension -eq '.pdb'){continue}
  $files.Add($relative,$file.FullName)
 }
 if($component -eq 'Server') {
  $base=(Resolve-Path 'artifacts/manager').Path
  foreach($file in Get-ChildItem -LiteralPath $base -Recurse -File) {
   if($file.Extension -eq '.pdb'){continue}
   $files.Add('Manager/'+$file.FullName.Substring($base.Length+1).Replace('\','/'),$file.FullName)
  }
  foreach($file in @('Service-Manage.ps1','Configure-Server.ps1')){$files.Add($file,(Join-Path (Resolve-Path 'deployment').Path $file))}
 }
 $files['Updater/TA.Updater.exe']=(Resolve-Path 'artifacts/updater/TA.Updater.exe').Path
 $files['Update-Application.ps1']=(Resolve-Path 'deployment/Update-Application.ps1').Path
 $files['THIRD-PARTY-NOTICES.txt']=(Resolve-Path 'deployment/THIRD-PARTY-NOTICES.txt').Path
 $specs=@(foreach($key in ($files.Keys | Sort-Object)) { $file=Get-Item -LiteralPath $files[$key]; @{Path=$key;Length=$file.Length;Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash} })
 $manifest=@{SchemaVersion=1;Component=$component;Version=$Version;Files=$specs}|ConvertTo-Json -Depth 5
 $destination=Join-Path $Output "TA-Commesse-$component-Update-$Version.zip"
 if(Test-Path -LiteralPath $destination){throw "Pacchetto gia presente: $destination"}
 $zip=[IO.Compression.ZipFile]::Open($destination,[IO.Compression.ZipArchiveMode]::Create)
 try {
  $entry=$zip.CreateEntry('update-manifest.json'); $writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
  try{$writer.Write($manifest)}finally{$writer.Dispose()}
  foreach($key in ($files.Keys|Sort-Object)){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$files[$key],'payload/'+$key,[IO.Compression.CompressionLevel]::Optimal)}
 } finally {$zip.Dispose()}
 Write-Output "Creato $destination"
}
Copy-Item 'artifacts/updater/TA.Updater.exe' (Join-Path $Output "TA-Commesse-Aggiornatore-$Version.exe") -Force
