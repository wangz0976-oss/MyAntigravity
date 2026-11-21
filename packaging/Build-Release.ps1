[CmdletBinding()]
param(
    [string]$DotNet = 'dotnet',
    [string]$Iscc = '',
    [string]$Notes = '',
    [uri]$GitCodeDownloadUrl
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $root '.work'
$stage = Join-Path $work 'release-stage'
$previous = Join-Path $work 'release-previous'
$final = Join-Path $root 'dist'
$buildTemp = Join-Path $work 'tmp'

function Confirm-LocalDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected directory: $full"
    }
    if (Test-Path -LiteralPath $full) {
        $item = Get-Item -Force -LiteralPath $full
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Unexpected directory type: $full"
        }
    }
}

function Clear-BuildDirectory([string]$Path) {
    Confirm-LocalDirectory $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

foreach ($path in @($work, $stage, $previous, $final, $buildTemp)) { Confirm-LocalDirectory $path }
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:DOTNET_CLI_HOME = $work
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $work 'nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $work 'nuget-http'

if (!$Iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Iscc = $command.Source }
    else { $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
}
if (!(Test-Path -LiteralPath $Iscc -PathType Leaf)) { throw 'Inno Setup 6 ISCC.exe is required. Pass -Iscc with its path.' }
$Iscc = (Resolve-Path -LiteralPath $Iscc).Path

[xml]$props = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw 'Directory.Build.props must contain a semantic version.'
}
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $entry = '## ' + $version
    $inside = $false
    $lines = foreach ($line in Get-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'CHANGELOG.md')) {
        if ($inside -and $line.StartsWith('## ', [StringComparison]::Ordinal)) { break }
        if ($line.TrimEnd() -ceq $entry) { $inside = $true; continue }
        if ($inside) { $line }
    }
    $Notes = ($lines -join [char]10).Trim()
    if (!$Notes) { throw "CHANGELOG.md needs a non-empty ## $version section for this release." }
}
$channel = if ($version.Contains('-')) { 'beta' } else { 'stable' }
if ($GitCodeDownloadUrl -and (!$GitCodeDownloadUrl.IsAbsoluteUri -or $GitCodeDownloadUrl.Scheme -ne 'https' -or
    $GitCodeDownloadUrl.UserInfo -or !$GitCodeDownloadUrl.AbsolutePath.EndsWith('/MyAntigravity-Setup.exe', [StringComparison]::Ordinal))) {
    throw 'GitCode download URL must be HTTPS and end in /MyAntigravity-Setup.exe.'
}

if ((Test-Path -LiteralPath $previous) -and !(Test-Path -LiteralPath $final)) {
    Move-Item -LiteralPath $previous -Destination $final
}
Clear-BuildDirectory $previous
Clear-BuildDirectory $stage
$app = Join-Path $stage 'app'
$licenses = Join-Path $app 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Push-Location $root
try {
    & $DotNet publish src/MyAntigravity.WinForms -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:DebugType=None -p:DebugSymbols=false -o $app
    if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)." }
    Copy-Item -LiteralPath 'LICENSE', 'THIRD_PARTY_NOTICES.md' -Destination $app

    $assets = Get-Content -Raw -LiteralPath 'src/MyAntigravity.WinForms/obj/project.assets.json' | ConvertFrom-Json
    foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $framework = $assets.project.frameworks.PSObject.Properties.Value
        $reference = $framework.downloadDependencies | Where-Object name -eq $id | Select-Object -First 1
        if (!$reference) { throw "Runtime pack reference not found: $id" }
        $packName = ($reference.version.Trim('[',']') -split ',')[0].Trim()
        $pack = Join-Path (Join-Path $env:NUGET_PACKAGES $id) $packName
        $files = @(Get-ChildItem -LiteralPath $pack -File | Where-Object Name -Match '^LICENSE(\.TXT)?$|^THIRD-PARTY-NOTICES\.TXT$')
        if (!$files.Count) { throw "Runtime license package not found: $id" }
        foreach ($file in $files) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licenses "$id-$($file.Name)")
        }
    }

    & $Iscc "/DReleaseVersion=$version" "/DPayloadFolder=$app" "/DOutputFolder=$stage" 'packaging/MyAntigravity.iss'
    if ($LASTEXITCODE) { throw "Inno Setup failed ($LASTEXITCODE)." }

    $installer = 'MyAntigravity-Setup.exe'
    $sha = (Get-FileHash -LiteralPath (Join-Path $stage $installer) -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        version = $version
        channel = $channel
        downloadUrl = "https://github.com/wangz0976-oss/MyAntigravity/releases/download/v$version/$installer"
        sha256 = $sha
        notes = $Notes
    }
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $stage "update-$channel.json"), ($manifest | ConvertTo-Json), $encoding)
    if ($GitCodeDownloadUrl) {
        $manifest.downloadUrl = $GitCodeDownloadUrl.AbsoluteUri
        [IO.File]::WriteAllText((Join-Path $stage "update-gitcode-$channel.json"), ($manifest | ConvertTo-Json), $encoding)
    }

    if (Test-Path -LiteralPath $final) { Move-Item -LiteralPath $final -Destination $previous }
    try { Move-Item -LiteralPath $stage -Destination $final }
    catch {
        if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $final }
        throw
    }
    Clear-BuildDirectory $previous
    Write-Output "Release $version -> $final"
}
finally {
    Pop-Location
    Clear-BuildDirectory $stage
}
