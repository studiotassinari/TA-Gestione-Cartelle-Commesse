# Eseguire sulla VM da PowerShell amministrativa dopo aver predisposto account di dominio.
[CmdletBinding()]
param(
    # Elenchi separati da punto e virgola. Nessun dominio, gruppo o percorso predefinito: ogni installazione indica i propri.
    [string]$AllowedGroups='',
    [string]$AllowedGroup='',
    [string]$Domain='',
    [string]$CollaboratorsGroup='',
    [string]$ManagementUsers='',
    [ValidateRange(1024,65535)][int]$Port=5443,
    [string]$JobsRoot='',
    [string]$BidsRoot='',
    [string]$ArchiveFolder='',
    [string]$JobTemplate='',
    [string]$ArchivedJobsRoot='',
    # Usato dal Gestore server dopo aver salvato il file: applica solo permessi e firewall.
    [switch]$KeepSettings,
    [switch]$OpenDomainFirewall
)
$ErrorActionPreference='Stop'
function Split-List([string]$text) { @($text.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique) }
$folder=Join-Path $env:ProgramData 'Tassinari\CommesseServer'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
# Solo amministratori e SYSTEM possono modificare le impostazioni; il servizio deve leggerle.
$acl=Get-Acl -LiteralPath $folder
$acl.SetAccessRuleProtection($true,$false)
foreach($sid in @('S-1-5-18','S-1-5-32-544')) {
    $rule=New-Object System.Security.AccessControl.FileSystemAccessRule([System.Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
}
$rule=New-Object System.Security.AccessControl.FileSystemAccessRule([System.Security.Principal.SecurityIdentifier]::new('S-1-5-11'),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $folder -AclObject $acl
$file=Join-Path $folder 'server-settings.json'
# Le proprietà non indicate restano quelle già salvate: una nuova esecuzione non azzera le altre impostazioni.
$root=if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $root.PSObject.Properties['Server']) { $root | Add-Member -NotePropertyName Server -NotePropertyValue ([pscustomobject]@{}) }
$server=$root.Server
function Set-Setting([string]$name,$value) { $server | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force }
function Get-Setting([string]$name) { if ($server.PSObject.Properties[$name]) { $server.$name } else { $null } }
if (-not $KeepSettings) {
    $groups=@(Split-List (($AllowedGroups,$AllowedGroup) -join ';'))
    if ($groups.Count -eq 0) { $groups=@(@(Get-Setting 'AllowedGroups') + @(Get-Setting 'AllowedGroup') | Where-Object { $_ } | Select-Object -Unique) }
    if ($groups.Count -eq 0) { throw 'Indicare almeno un gruppo autorizzato con -AllowedGroups.' }
    $managers=if ($PSBoundParameters.ContainsKey('ManagementUsers')) { @(Split-List $ManagementUsers) } else { @(Get-Setting 'ManagementUsers' | Where-Object { $_ }) }
    if ($PSBoundParameters.ContainsKey('CollaboratorsGroup')) { Set-Setting 'CollaboratorsGroup' $CollaboratorsGroup.Trim() }
    if ($PSBoundParameters.ContainsKey('Domain')) { Set-Setting 'Domain' $Domain.Trim() }
    # Verifica l'esistenza dei nomi senza concedere o cambiare permessi nel dominio.
    foreach($name in @($groups + $managers + @(Get-Setting 'CollaboratorsGroup') | Where-Object { $_ })) {
        [void](New-Object System.Security.Principal.NTAccount($name)).Translate([System.Security.Principal.SecurityIdentifier])
    }
    Set-Setting 'AllowedGroups' ([string[]]$groups)
    # Il primo gruppo resta anche nella proprietà storica, per poter tornare a una versione precedente del servizio.
    Set-Setting 'AllowedGroup' ([string]$groups[0])
    Set-Setting 'ManagementUsers' ([string[]]@($managers | Where-Object { $_ }))
    foreach($pair in @(@('JobsRoot',$JobsRoot),@('BidsRoot',$BidsRoot),@('ArchiveFolder',$ArchiveFolder),@('JobTemplate',$JobTemplate),@('ArchivedJobsRoot',$ArchivedJobsRoot))) {
        if ($pair[1]) { Set-Setting $pair[0] $pair[1] }
    }
    if ($PSBoundParameters.ContainsKey('Port') -or -not (Get-Setting 'Port')) { Set-Setting 'Port' $Port }
    if (-not (Get-Setting 'RefreshMinutes')) { Set-Setting 'RefreshMinutes' 60 }
    foreach($name in @('JobsRoot','BidsRoot','ArchivedJobsRoot')) { if (-not ([string](Get-Setting $name)).StartsWith('\\')) { throw "Indicare -$name come percorso UNC." } }
    if (-not (Get-Setting 'ArchiveFolder')) { throw 'Indicare -ArchiveFolder.' }
    $root | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $file -Encoding UTF8
}
elseif (-not (Test-Path -LiteralPath $file)) { throw 'Configurazione assente: salvarla prima dal Gestore server.' }
$activePort=if (Get-Setting 'Port') { [int](Get-Setting 'Port') } else { 5443 }
if ($activePort -in @(8080,8443)) { throw 'Scegliere una porta TCP dedicata, non una porta web.' }
if ($OpenDomainFirewall) {
    # Regola limitata al profilo di dominio, sottorete locale e all'eseguibile del servizio.
    $ruleName='TACommesseServer-TCP'
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -Name $ruleName }
    New-NetFirewallRule -Name $ruleName -DisplayName 'TA Commesse Server TCP dedicato' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $activePort -Profile Domain -RemoteAddress LocalSubnet -Program (Join-Path $PSScriptRoot 'TA.Server.exe') | Out-Null
}
Write-Output 'Configurazione salvata. Verificare account, SPN e permessi sulle condivisioni prima di avviare TACommesseServer.'
