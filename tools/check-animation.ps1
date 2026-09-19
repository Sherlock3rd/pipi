param(
    [Parameter(Mandatory=$true)][string]$Python,
    [string]$OpenCvPath,
    [string]$Clips='all',
    [string]$Output='artifacts/animation-monitor/latest'
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    if($OpenCvPath){$env:PYTHONPATH=$OpenCvPath}
    & $Python tools/check_animation_continuity.py --self-test
    if($LASTEXITCODE -ne 0){throw 'Monitor self-test failed'}
    & .tools/dotnet/dotnet.exe publish src/Chenpi -c Release -r win-x64 --self-contained true -o "$Output/publish" --no-restore
    if($LASTEXITCODE -ne 0){throw 'Audit build failed'}
    $taskOutput=[IO.Path]::GetFullPath($Output)
    $taskArgs=@('--audit-animations',('"'+$taskOutput+'/frames"'))
    if($Clips -ne 'all'){$taskArgs+=@('--audit-clips',$Clips)}
    $taskProcess=Start-Process -FilePath "$taskOutput/publish/Chenpi.exe" -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $taskProcess.WaitForExit()
    if($taskProcess.ExitCode -ne 0){throw "WPF export failed; inspect $Output/frames/error.txt"}
    & $Python tools/check_animation_continuity.py "$Output/frames" --out "$Output/report"
    $taskResult=$LASTEXITCODE
    Write-Output "Open $taskOutput/report/index.html to review actual adjacent frames."
    exit $taskResult
} finally {Pop-Location}
