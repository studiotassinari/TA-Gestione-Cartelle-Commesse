[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][ValidatePattern('^tunnel_[a-zA-Z0-9]+$')][string]$TunnelId,
    [ValidateRange(1024,65535)][int]$Port = 5443,
    [ValidateSet('kerberos','local')][string]$Transport = 'kerberos',
    [switch]$CheckConnection,
    [string]$TunnelClient = ''
)
$ErrorActionPreference = 'Stop'
# Risolve il percorso dopo il binding dei parametri, anche con Windows PowerShell 5.1.
if ([string]::IsNullOrWhiteSpace($TunnelClient)) {
    $TunnelClient = Join-Path $PSScriptRoot 'Tunnel\tunnel-client.exe'
}
# Nessun segreto persistente: richiesta nascosta e variabile limitata al processo.
$mcp = Join-Path $PSScriptRoot 'Mcp\TA.Mcp.exe'
if (!(Test-Path -LiteralPath $mcp) -or !(Test-Path -LiteralPath $TunnelClient)) {
    throw 'Pacchetto incompleto: verificare Mcp\TA.Mcp.exe e Tunnel\tunnel-client.exe.'
}
$env:TA_MCP_SERVER = $Server
$env:TA_MCP_PORT = $Port.ToString()
$env:TA_MCP_TRANSPORT = $Transport
if ($CheckConnection) {
    & $mcp --check
    if ($LASTEXITCODE -ne 0) { throw 'Canale locale non disponibile. Aggiornare e avviare TACommesseServer prima del tunnel.' }
}
# Il profilo memorizza il percorso dell'eseguibile: cambia quando cambia pacchetto o posizione.
$digest = [Security.Cryptography.SHA256]::Create()
try {
    $identityText = $mcp.ToUpperInvariant() + '|' + (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Mcp\TA.Mcp.dll')).Hash
    $packageId = ([BitConverter]::ToString($digest.ComputeHash([Text.Encoding]::UTF8.GetBytes($identityText)))).Replace('-','').Substring(0,12).ToLowerInvariant()
} finally { $digest.Dispose() }
$profile = 'ta-commesse-' + $TunnelId + '-' + $Transport + '-' + $packageId
$profileDir = Join-Path $env:LOCALAPPDATA ('TA-Commesse\McpTunnel\' + $profile)
if (!(Test-Path -LiteralPath (Join-Path $profileDir ($profile + '.yaml')))) {
    # Il parser del tunnel accetta apici singoli, preservati anche da Windows PowerShell 5.
    $quote = [string][char]39
    $mcpCommand = $quote + $mcp.Replace('\','/').Replace($quote, ($quote + '\' + $quote + $quote)) + $quote
    & $TunnelClient init --sample sample_mcp_stdio_local --profile $profile --profile-dir $profileDir --tunnel-id $TunnelId --mcp-command $mcpCommand --health-listen-addr '127.0.0.1:0'
    if ($LASTEXITCODE -ne 0) { throw 'Creazione profilo non riuscita.' }
}
$previousKey = $env:CONTROL_PLANE_API_KEY
$secret = Read-Host 'Chiave runtime del tunnel (non viene mostrata o salvata)' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $env:CONTROL_PLANE_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    & $TunnelClient doctor --profile $profile --profile-dir $profileDir --explain
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostica tunnel non riuscita; avvio annullato.' }
    Write-Host 'Avvio tunnel. Lasciare aperta questa finestra durante la prova con ChatGPT. Ctrl+C per terminare.'
    & $TunnelClient run --profile $profile --profile-dir $profileDir
    if ($LASTEXITCODE -ne 0) { throw 'Il tunnel si è arrestato con un errore.' }
} finally {
    $env:CONTROL_PLANE_API_KEY = $previousKey
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    $secret.Dispose()
}
