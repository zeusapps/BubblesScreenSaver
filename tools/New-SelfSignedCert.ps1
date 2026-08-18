<#
.SYNOPSIS
    Creates a self-signed code-signing certificate and prints the two repository secrets the
    release workflow looks for.

.DESCRIPTION
    This is for exercising the signing path end to end. A self-signed certificate does NOT
    stop SmartScreen warning users: Windows trusts a signature only when it chains to a CA in
    its trusted root store, and you cannot put yourself there on someone else's machine.
    For a signature that actually helps, see "Code signing" in the README.

.EXAMPLE
    .\tools\New-SelfSignedCert.ps1 -Password 'choose-something'
#>
param(
    [Parameter(Mandatory)][string]$Password,
    [string]$Subject = 'CN=zeusapps',
    [int]$Years = 3
)

$ErrorActionPreference = 'Stop'

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears($Years) `
    -KeyUsage DigitalSignature `
    -KeyLength 3072

$pfx = Join-Path $env:TEMP 'bubbles-signing.pfx'
$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx))
Remove-Item $pfx -Force

Write-Host ''
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host ''
Write-Host 'Add these as repository secrets (Settings -> Secrets and variables -> Actions):'
Write-Host '  SIGNING_PFX_BASE64   = (the base64 below)'
Write-Host '  SIGNING_PFX_PASSWORD = (the password you passed in)'
Write-Host ''
Set-Clipboard -Value $base64
Write-Host 'The base64 blob has been copied to your clipboard.'
