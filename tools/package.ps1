<#
.SYNOPSIS
    Builds the release files into artifacts\release.

.DESCRIPTION
    All builds are x64 and self-contained (.NET and Windows App SDK included), so users
    don't have to install any runtime first.

        YogaKeyboardMapper-<ver>-x64-setup.exe   per-user installer (Inno Setup 6)
        YogaKeyboardMapper-<ver>-x64.zip / .7z   portable
        YogaKeyboardMapper-<ver>-x64.msix        signed with a self-signed certificate
        YogaKeyboardMapper.cer                   that certificate (users must trust it once)
        YogaKeyboardMapper-<ver>-x64-store.msix  unsigned, Partner Center identity
                                                 (only with -StoreIdentityName / -StorePublisher)
        SHA256SUMS.txt

    Needs: .NET 8 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup), 7-Zip or NanaZip.
    makeappx / signtool come from the Microsoft.Windows.SDK.BuildTools NuGet package.

        powershell -ExecutionPolicy Bypass -File tools\package.ps1 -NewCertificate   # first time
        powershell -ExecutionPolicy Bypass -File tools\package.ps1

.PARAMETER CertificateThumbprint
    Signing certificate in Cert:\CurrentUser\My. Its subject must equal the Publisher in
    Package.appxmanifest. Defaults to the newest certificate with that subject.

.PARAMETER NewCertificate
    Create a self-signed code-signing certificate (valid 5 years) for that Publisher in
    Cert:\CurrentUser\My. Keep it: updates must be signed with the same certificate.

.PARAMETER StoreIdentityName
    Package/Identity/Name from Partner Center (Product identity page).

.PARAMETER StorePublisher
    Package/Identity/Publisher from Partner Center, e.g. "CN=01234567-89AB-...".

.PARAMETER StorePublisherDisplayName
    Package/Properties/PublisherDisplayName from Partner Center.

.PARAMETER StoreDisplayName
    App name reserved in Partner Center; the Store package's display name must match it.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\artifacts\release'),
    [string]$CertificateThumbprint = '',
    [switch]$NewCertificate,
    [string]$StoreIdentityName = '',
    [string]$StorePublisher = '',
    [string]$StorePublisherDisplayName = '',
    [string]$StoreDisplayName = 'Keyboard Mapper for Yoga Book'
)

$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src\YogaKeyboardMapper\YogaKeyboardMapper.csproj'
$manifestPath = Join-Path $root 'src\YogaKeyboardMapper\Package.appxmanifest'
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$work = Join-Path $OutDir 'work'

# Native tools are called directly (PowerShell would mangle "-p:Name=value" when
# forwarding through a function), followed by this check.
function Assert-Success([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE" }
}

function Find-Tool([string]$name, [string[]]$candidates) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw "$name not found"
}

# Unused parts of the Windows App SDK (Windows AI / ML); ~40 MB the app never loads.
function Remove-UnusedRuntimeFiles([string]$dir) {
    foreach ($f in 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'DirectML.dll') {
        Remove-Item (Join-Path $dir $f) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item (Join-Path $dir 'NpuDetect') -Recurse -Force -ErrorAction SilentlyContinue
}

# --- version, tools ----------------------------------------------------------------------

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
$base = "YogaKeyboardMapper-$version-x64"
Write-Host "version $version"

$sevenZip = Find-Tool '7z' @("$env:ProgramFiles\7-Zip\7z.exe")
$iscc = Find-Tool 'ISCC' @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe")

$buildTools = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x64$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $buildTools) { throw 'makeappx.exe not found; run "dotnet restore" first.' }
$makeappx = $buildTools.FullName
$signtool = Join-Path $buildTools.DirectoryName 'signtool.exe'

# --- signing certificate -----------------------------------------------------------------

$manifestXml = [xml](Get-Content $manifestPath -Raw -Encoding UTF8)
$publisher = $manifestXml.Package.Identity.Publisher

if ($NewCertificate) {
    $created = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
        -FriendlyName 'Yoga Keyboard Mapper code signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(5)
    Write-Host "created certificate $($created.Thumbprint) for $publisher"
}

$cert = if ($CertificateThumbprint) {
    Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
} else {
    Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotBefore -Descending | Select-Object -First 1
}
if (-not $cert) { throw "No signing certificate for '$publisher' in Cert:\CurrentUser\My. Run with -NewCertificate." }
if ($cert.Subject -ne $publisher) { throw "Certificate subject '$($cert.Subject)' doesn't match manifest Publisher '$publisher'." }
Write-Host "signing with $($cert.Thumbprint) ($($cert.Subject), expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"

# --- portable build: zip, 7z, setup.exe --------------------------------------------------

Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null

$portable = Join-Path $work 'YogaKeyboardMapper'
Write-Host "`n== publish (unpackaged)"
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -o $portable -v q -nologo
Assert-Success 'dotnet publish'
Remove-UnusedRuntimeFiles $portable

Write-Host "`n== zip / 7z"
Push-Location $work
try {
    & $sevenZip a -tzip -mx=9 (Join-Path $OutDir "$base.zip") 'YogaKeyboardMapper' | Out-Null
    Assert-Success '7z (zip)'
    & $sevenZip a -t7z -mx=9 (Join-Path $OutDir "$base.7z") 'YogaKeyboardMapper' | Out-Null
    Assert-Success '7z (7z)'
}
finally {
    Pop-Location
}

Write-Host "`n== setup.exe"
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$portable" "/O$OutDir" (Join-Path $root 'installer\YogaKeyboardMapper.iss')
Assert-Success 'ISCC'

# --- MSIX --------------------------------------------------------------------------------

Write-Host "`n== publish (MSIX)"
$msixBuild = Join-Path $work 'msix-build\'
# Separate obj/bin folders so the packaged build never mixes with the unpackaged one.
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:WindowsPackageType=MSIX `
    -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never -p:AppxSymbolPackageEnabled=false `
    "-p:AppxPackageDir=$msixBuild" -p:IntermediateOutputPath=obj\msix\ -p:OutputPath=bin\msix\ -v q -nologo
Assert-Success 'dotnet publish (MSIX)'
$built = Get-ChildItem $msixBuild -Recurse -Filter *.msix | Select-Object -First 1
if (-not $built) { throw 'MSIX build produced no package' }

# Unpack, drop the unused runtime files, then repack with the release version.
$layout = Join-Path $work 'msix-layout'
& $makeappx unpack /p $built.FullName /d $layout /o | Out-Null
Assert-Success 'makeappx unpack'
Remove-UnusedRuntimeFiles $layout
foreach ($f in 'AppxBlockMap.xml', 'AppxSignature.p7x', '[Content_Types].xml') {
    Remove-Item -LiteralPath (Join-Path $layout $f) -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath (Join-Path $layout 'AppxMetadata') -Recurse -Force -ErrorAction SilentlyContinue

$layoutManifest = Join-Path $layout 'AppxManifest.xml'
function Set-Identity([string]$name, [string]$pub, [string]$pubDisplay, [string]$display) {
    $xml = [xml](Get-Content $layoutManifest -Raw -Encoding UTF8)
    $xml.Package.Identity.SetAttribute('Version', "$version.0")
    if ($name) { $xml.Package.Identity.SetAttribute('Name', $name) }
    if ($pub) { $xml.Package.Identity.SetAttribute('Publisher', $pub) }
    if ($pubDisplay) { $xml.Package.Properties.PublisherDisplayName = $pubDisplay }
    if ($display) {
        $xml.Package.Properties.DisplayName = $display
        $app = $xml.Package.Applications.Application
        $app.VisualElements.SetAttribute('DisplayName', $display)
        $app.Extensions.Extension.StartupTask.SetAttribute('DisplayName', $display)
    }
    $xml.Save($layoutManifest)
}

Write-Host "`n== msix (self-signed)"
Set-Identity '' '' '' ''
$msix = Join-Path $OutDir "$base.msix"
& $makeappx pack /d $layout /p $msix /h SHA256 /o | Out-Null
Assert-Success 'makeappx pack'
$signArgs = @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, '/s', 'My')
& $signtool @signArgs /tr 'http://timestamp.digicert.com' /td SHA256 $msix
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'Timestamping failed; signing without a timestamp.'
    & $signtool @signArgs $msix
    Assert-Success 'signtool'
}
Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'YogaKeyboardMapper.cer') | Out-Null

if ($StoreIdentityName -or $StorePublisher) {
    if (-not ($StoreIdentityName -and $StorePublisher)) { throw 'Pass both -StoreIdentityName and -StorePublisher.' }
    Write-Host "`n== msix (Store, unsigned)"
    Set-Identity $StoreIdentityName $StorePublisher $StorePublisherDisplayName $StoreDisplayName
    & $makeappx pack /d $layout /p (Join-Path $OutDir "$base-store.msix") /h SHA256 /o | Out-Null
    Assert-Success 'makeappx pack (Store)'
}

# --- checksums ---------------------------------------------------------------------------

Remove-Item $work -Recurse -Force
$files = Get-ChildItem $OutDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name
$sums = foreach ($f in $files) { '{0}  {1}' -f (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $f.Name }
[System.IO.File]::WriteAllLines((Join-Path $OutDir 'SHA256SUMS.txt'), [string[]]$sums)

Write-Host "`n== done: $OutDir"
$files | ForEach-Object { '{0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
