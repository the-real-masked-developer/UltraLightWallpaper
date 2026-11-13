[CmdletBinding()]
param([string]$OutputPath = '.\artifacts\test-video.mp4', [ValidateSet(24,25,30,60)][int]$Fps = 30, [int]$Seconds = 8)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
ffmpeg -hide_banner -loglevel error -f lavfi -i "testsrc2=size=2560x1440:rate=$Fps" -t $Seconds -c:v libx264 -preset ultrafast -crf 28 -threads 4 -pix_fmt yuv420p -an -y $target
if ($LASTEXITCODE -ne 0) { throw 'Test video generation failed. Install ffmpeg or supply your own local test clip.' }
Write-Host $target
