[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path (Split-Path $projectRoot -Parent) 'UltraLightWallpaper-win-x64' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
Push-Location $projectRoot
try {
    dotnet restore src/UltraLightWallpaper.Windows -r win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    dotnet publish src/UltraLightWallpaper.Windows -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; no release archive was produced.' }
    $executable = Join-Path $destination 'UltraLightWallpaper.exe'
    if (!(Test-Path -LiteralPath $executable)) { throw 'Published executable is missing.' }
    $longPlugin = Get-ChildItem -LiteralPath (Join-Path $destination 'libvlc\win-x64\plugins') -Filter '*.dll' -File -Recurse | Where-Object { $_.FullName.Length -ge 260 } | Select-Object -First 1
    if ($longPlugin) { throw 'The output directory is too long for libVLC 3. Choose a shorter OutputDirectory.' }
    # The cache contains relative plug-in paths. Validate native bytes and timestamps before reusing it.
    $cacheRoot = Join-Path $projectRoot 'third_party/runtime-cache'
    $cacheManifest = Get-Content -LiteralPath (Join-Path $cacheRoot 'plugins-manifest.json') -Raw | ConvertFrom-Json
    $pluginsRoot = Join-Path $destination 'libvlc/win-x64/plugins'
    if (@(Get-ChildItem -LiteralPath $pluginsRoot -Filter '*.dll' -File -Recurse).Count -ne $cacheManifest.files.Count) { throw 'Unexpected native plug-in count; regenerate the cache.' }
    foreach ($entry in $cacheManifest.files) {
        $pluginFile = Get-Item -LiteralPath (Join-Path $pluginsRoot $entry.path)
        $mtime = ([DateTimeOffset]$pluginFile.LastWriteTimeUtc).ToUnixTimeSeconds()
        if ($pluginFile.Length -ne $entry.size -or $mtime -ne $entry.mtime -or (Get-FileHash -LiteralPath $pluginFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
            throw 'The native plug-ins differ from the validated cache. Regenerate it with --prepare-native-cache and update its manifest.'
        }
    }
    Copy-Item -LiteralPath (Join-Path $cacheRoot 'vlc-3.0.24-win-x64-plugins.dat') -Destination (Join-Path $pluginsRoot 'plugins.dat') -Force
    Copy-Item -LiteralPath 'README.md','THIRD-PARTY-NOTICES.md' -Destination $destination -Force
    Copy-Item -LiteralPath 'docs','third_party' -Destination $destination -Recurse -Force
    $assets = Get-Content -LiteralPath 'src/UltraLightWallpaper.Windows/obj/project.assets.json' -Raw | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $destination 'UltraLightWallpaper.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packageName = ($framework.name + '.runtime.win-x64').ToLowerInvariant()
        $packageDirectory = Join-Path (Join-Path $packageRoot $packageName) $framework.version
        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)') {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $destination "third_party/licenses/$packageName-$($notice.Name)") -Force
        }
    }
    $hashes = Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace('\','/')
        "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() )  $relative"
    }
    $hashes | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding utf8
    Compress-Archive -LiteralPath $destination -DestinationPath ($destination + '.zip') -Force
    Write-Host "Built release: $executable"
    Write-Host "Archive: $destination.zip"
} finally { Pop-Location }
