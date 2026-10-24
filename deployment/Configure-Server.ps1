# Eseguire sulla VM da PowerShell amministrativa dopo aver predisposto account di dominio.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$AllowedGroup,
    [ValidateRange(1024,65535)][int]$Port=5443,
    [string]$JobsRoot='\\ingtassinari.it\data\lavori',
    [string]$BidsRoot='\\ingtassinari.it\data\gare',
    [string]$ArchiveFolder='\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE',
    [string]$JobTemplate="\\ingtassinari.it\data\lavori\2026\C26198 - CLIENTE - LUOGO - COSA",
    [string]$ArchivedJobsRoot="\\ingtassinari.it\data\archivio", [switch]$OpenDomainFirewall
)
$ErrorActionPreference='Stop'
if ($Port -in @(8080,8443)) { throw 'Scegliere una porta TCP dedicata, non una porta web.' }
foreach($path in @($JobsRoot,$BidsRoot,$ArchiveFolder)) { if (!$path.StartsWith('\\')) { throw 'Usare percorsi UNC.' } }
# Verifica l'esistenza del gruppo senza concedere o cambiare permessi nel dominio.
$account=New-Object System.Security.Principal.NTAccount($AllowedGroup)
[void]$account.Translate([System.Security.Principal.SecurityIdentifier])
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
$settings=@{ Server=@{ JobsRoot=$JobsRoot; BidsRoot=$BidsRoot; ArchiveFolder=$ArchiveFolder; AllowedGroup=$AllowedGroup; Port=$Port; RefreshMinutes=1; JobTemplate=$JobTemplate; ArchivedJobsRoot=$ArchivedJobsRoot } }
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'server-settings.json') -Encoding UTF8
if ($OpenDomainFirewall) {
    # Regola limitata al profilo di dominio, sottorete locale e all'eseguibile del servizio.
    $ruleName='TACommesseServer-TCP'
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -Name $ruleName }
    New-NetFirewallRule -Name $ruleName -DisplayName 'TA Commesse Server TCP dedicato' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Domain -RemoteAddress LocalSubnet -Program (Join-Path $PSScriptRoot 'TA.Server.exe') | Out-Null
}
Write-Output 'Configurazione salvata. Verificare account, SPN e permessi sulle condivisioni prima di avviare TACommesseServer.'
