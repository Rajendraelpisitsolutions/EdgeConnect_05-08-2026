# ============================================================================
# File: tools/sparkplug-proto/regenerate.ps1
# Purpose: Regenerate (or verify) the vendored C# protobuf types for the
#          Sparkplug B sink from the pinned sparkplug_b.proto.
#          Implements ADR-0035 Rule 2: no Tahu runtime dependency; generated
#          code is vendored, never hand-edited, and regeneration is an
#          explicit, reviewed step.
# Usage:
#   pwsh tools/sparkplug-proto/regenerate.ps1            # regenerate in place
#   pwsh tools/sparkplug-proto/regenerate.ps1 -Verify    # fail (exit 1) if
#                                                        # regeneration differs
#                                                        # from the vendored file
# Provenance: docs/compliance/sparkplug-b-proto-provenance.md
# ============================================================================
[CmdletBinding()]
param(
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'

# --- Pinned values (change ONLY alongside the provenance record) -------------
$PinnedProtoSha256 = '4432C5C483B7FB9732D0594C98A2E97DCA5E517E39C5374A8B918D837F0B4A19'
$ProtobufToolsVersion = '3.35.1'   # Google.Protobuf.Tools (protoc); matches the Google.Protobuf runtime pin
$CsharpOptions = 'internal_access,file_extension=.g.cs'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$ProtoDir = Join-Path $RepoRoot 'src/ElpisEdgeConnect.Sinks.SparkplugB/Protos'
$ProtoFile = Join-Path $ProtoDir 'sparkplug_b.proto'
$OutputDir = Join-Path $RepoRoot 'src/ElpisEdgeConnect.Sinks.SparkplugB/Protobuf'
$GeneratedFile = Join-Path $OutputDir 'SparkplugB.g.cs'
$PackagesDir = Join-Path $PSScriptRoot 'packages'

# --- 1. Guard: the pinned schema must be byte-identical to the record --------
$actualSha = (Get-FileHash $ProtoFile -Algorithm SHA256).Hash
if ($actualSha -ne $PinnedProtoSha256) {
    Write-Error ("sparkplug_b.proto SHA-256 mismatch.`n  expected: $PinnedProtoSha256`n  actual:   $actualSha`n" +
        'The pinned schema must never be edited. Restore it from the pinned Tahu commit, ' +
        'or (for a deliberate re-pin) update the provenance record and this script together.')
}

# --- 2. Obtain the pinned protoc via NuGet (Google.Protobuf.Tools) -----------
$stubDir = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force $stubDir | Out-Null
$stubProj = Join-Path $stubDir 'protoc-restore.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Google.Protobuf.Tools" Version="$ProtobufToolsVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content $stubProj -Encoding utf8

dotnet restore $stubProj --packages $PackagesDir | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error 'dotnet restore of Google.Protobuf.Tools failed.' }

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($architecture) {
    'X64'   { $arch = 'x64' }
    'Arm64' { $arch = 'aarch64' }
    default { throw "Unsupported protoc host architecture: $architecture (supported: X64, Arm64)" }
}
$osDir = if ($IsWindows -or $env:OS -eq 'Windows_NT') { "windows_$arch" }
         elseif ($IsMacOS) { "macosx_$arch" }
         else { "linux_$arch" }
$protocName = if ($osDir -like 'windows_*') { 'protoc.exe' } else { 'protoc' }
$protoc = Join-Path $PackagesDir "google.protobuf.tools/$ProtobufToolsVersion/tools/$osDir/$protocName"
if (-not (Test-Path $protoc)) { Write-Error "protoc not found at $protoc" }
if ($osDir -notlike 'windows_*') { chmod +x $protoc }

$protocVersion = (& $protoc --version).Trim()
Write-Host "Using $protocVersion (Google.Protobuf.Tools $ProtobufToolsVersion)"

# --- 3. Generate to a temp directory -----------------------------------------
$tempOut = Join-Path $stubDir 'generated'
if (Test-Path $tempOut) { Remove-Item -Recurse -Force $tempOut }
New-Item -ItemType Directory -Force $tempOut | Out-Null

& $protoc --proto_path=$ProtoDir --csharp_out=$tempOut --csharp_opt=$CsharpOptions sparkplug_b.proto
if ($LASTEXITCODE -ne 0) { Write-Error 'protoc generation failed.' }

$tempFile = Join-Path $tempOut 'SparkplugB.g.cs'
if (-not (Test-Path $tempFile)) { Write-Error "Expected generated file missing: $tempFile" }

# --- 4. Verify or install -----------------------------------------------------
if ($Verify) {
    if (-not (Test-Path $GeneratedFile)) {
        Write-Error "Vendored file missing: $GeneratedFile (run without -Verify to create it)."
    }
    # Byte-level comparison (SHA-256), never decoded-string comparison: encoding
    # differences (e.g. a BOM) must fail the gate, matching the byte-identical
    # claim in the provenance record.
    $expectedHash = (Get-FileHash $GeneratedFile -Algorithm SHA256).Hash
    $actualHash = (Get-FileHash $tempFile -Algorithm SHA256).Hash
    if ($expectedHash -ne $actualHash) {
        Write-Error ("Vendored generated code differs from regeneration (byte-level).`n" +
            "  committed:   $expectedHash`n" +
            "  regenerated: $actualHash`n" +
            'Run tools/sparkplug-proto/regenerate.ps1 (no -Verify) and review the change.')
    }
    Write-Host "Verify OK: vendored generated code is byte-identical to regeneration (SHA-256 $expectedHash)."
}
else {
    New-Item -ItemType Directory -Force $OutputDir | Out-Null
    Copy-Item $tempFile $GeneratedFile -Force
    $generatedHash = (Get-FileHash $GeneratedFile -Algorithm SHA256).Hash
    Write-Host "Regenerated: $GeneratedFile"
    Write-Host "Record in provenance: proto SHA-256 $PinnedProtoSha256, generated SHA-256 $generatedHash, $protocVersion, Google.Protobuf.Tools $ProtobufToolsVersion, csharp_opt=$CsharpOptions"
}
