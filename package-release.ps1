param(
    [string]$Dotnet = 'dotnet',
    [string]$PenumbraLibPath = "$env:APPDATA/XIVLauncher/installedPlugins/Penumbra/1.7.2.1",
    [string]$DalamudLibPath = '',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root ('bin/licensed-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null

# The allowlist avoids packaging game assets, credentials, reports, build output or local mods.
$files = @(Get-ChildItem $root -File | Where-Object { $_.Extension -in '.cs','.csproj','.md','.ps1' -or $_.Name -in 'LICENSE','packages.lock.json','.gitignore' })
foreach ($dir in 'tests','relay','assets','third_party') {
    $files += @(Get-ChildItem (Join-Path $root $dir) -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|__pycache__|reports|staging)[\\/]' -and
        ($_.Extension -in '.cs','.csproj','.py','.md','.txt','.png','.json','.service','.zip' -or $_.Name -in 'LICENSE','Caddyfile')
    })
}
# Source ZIPs are the only permitted binary archives in this snapshot.
if ($files | Where-Object { $_.Extension -eq '.zip' -and $_.DirectoryName -ne (Join-Path $root 'third_party/source') }) { throw 'Unexpected source archive.' }
$hashes = @{}
foreach ($file in $files) { $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$argsBuild = @('build', (Join-Path $root 'EmoteShelf.csproj'), '-c', 'Release', "-p:OutputPath=$build/", "-p:PenumbraLibPath=$PenumbraLibPath", '-v:q')
if ($NoRestore) { $argsBuild += '--no-restore' }
if ($DalamudLibPath) { $argsBuild += "-p:DalamudLibPath=$DalamudLibPath/" }
& $Dotnet @argsBuild
if ($LASTEXITCODE -ne 0) { throw 'Build failed; no release package created.' }
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne $hashes[$file.FullName]) { throw 'Sources changed during build; retry.' }
}
$pins = @{
    'Luna.dll' = '05C7745656EDA434423790F967DCFB8EBC93194549DA080B9B05D69BEC2B8E6F'
    'Penumbra.Api.dll' = '393D4EFDF3A1B9585C57EDA615147C3B1E59FA7E678EA5E1DB95C8849659EC41'
    'third_party/source/Luna.zip' = '8033E413EA7EF0E12FDB557975C8E8611AB1D1881EA484A4DCED40CA1C6F0C4D'
    'third_party/source/Penumbra.Api.zip' = '9ED6CBA5511A2359D65D3F281A4FD4B72E05997C8D3BCA7B382A31E036D9657C'
    'third_party/source/ImSharp.zip' = 'A9FCA24112C80411A82F6032D8F956BD50656B77B0CBF6654DD9122452266CB6'
}
foreach ($pin in $pins.GetEnumerator()) {
    $path = if ($pin.Key.EndsWith('.dll')) { Join-Path $build $pin.Key } else { Join-Path $root $pin.Key }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $pin.Value) { throw "Dependency/source mismatch: $($pin.Key). Audit and update sources before release." }
}

Add-Type -AssemblyName System.IO.Compression
function Add-ZipFile($archive, $path, $name) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $path, $name.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$sourcePath = Join-Path $build 'SOURCE.zip'
$archive = [System.IO.Compression.ZipFile]::Open($sourcePath, 'Create')
try {
    foreach ($file in $files) { Add-ZipFile $archive $file.FullName ([System.IO.Path]::GetRelativePath($root, $file.FullName)) }
} finally { $archive.Dispose() }

# Other automatically copied DLLs are supplied by Dalamud and are not redistributed.
$runtime = @('EmoteShelf.dll','EmoteShelf.json','Luna.dll','Penumbra.Api.dll',
    'Microsoft.Extensions.DependencyInjection.dll','Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Extensions.Logging.Abstractions.dll','System.IO.Hashing.dll')
$release = Join-Path $build 'EmoteShelf-licensed.zip'
$archive = [System.IO.Compression.ZipFile]::Open($release, 'Create')
try {
    foreach ($name in $runtime) { Add-ZipFile $archive (Join-Path $build $name) $name }
    Add-ZipFile $archive $sourcePath 'SOURCE.zip'
    foreach ($name in 'LICENSE','THIRD-PARTY-NOTICES.md','BUILDING.md') { Add-ZipFile $archive (Join-Path $root $name) $name }
    foreach ($file in Get-ChildItem (Join-Path $root 'third_party/licenses') -File) { Add-ZipFile $archive $file.FullName ('licenses/' + $file.Name) }
    Add-ZipFile $archive (Join-Path $root 'third_party/NoireLib/LICENSE') 'licenses/NoireLib.txt'
} finally { $archive.Dispose() }
Write-Output "Release (use this archive, not the SDK's latest.zip): $release"
Get-FileHash -LiteralPath $release, $sourcePath
