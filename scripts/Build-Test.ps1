[CmdletBinding()]
param([switch]$Native, [string]$Video, [string]$LoopVideo, [string]$SecondVideo, [switch]$FullDuration, [switch]$Layout)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    dotnet restore UltraLightWallpaper.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet build UltraLightWallpaper.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet test tests/UltraLightWallpaper.Tests -c Release --no-build --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
    if ($Native -or $Video -or $LoopVideo -or $Layout) {
        # A short output path also avoids libVLC 3's MAX_PATH limit for plugin loading.
        dotnet publish tests/UltraLightWallpaper.Integration -c Release --no-restore --self-contained false -o artifacts/native-tests
        if ($LASTEXITCODE -ne 0) { throw 'Native harness publish failed.' }
        $nativeArguments = @('artifacts/native-tests/UltraLightWallpaper.Integration.dll', '--report', 'artifacts/test-results/native-acceptance.json')
        if ($Native) { $nativeArguments += '--native-tests' }
        if ($Video) { $nativeArguments += @('--playback', [IO.Path]::GetFullPath($Video)) }
        if ($LoopVideo) { $nativeArguments += @('--loop-tests', [IO.Path]::GetFullPath($LoopVideo)) }
        if ($SecondVideo) { $nativeArguments += @('--second-video', [IO.Path]::GetFullPath($SecondVideo)) }
        if ($FullDuration) { $nativeArguments += '--full-duration' }
        if ($Layout) { $nativeArguments += '--ui-tests' }
        dotnet @nativeArguments
        if ($LASTEXITCODE -ne 0) { throw 'Native acceptance did not pass. Inspect the report or Windows application control policy.' }
    }
} finally { Pop-Location }
