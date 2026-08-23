param(
    [string]$Project = "SKD750Control.csproj",
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-VsDevCmd {
    $candidates = @(
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2026\Community\Common7\Tools\VsDevCmd.bat",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2026\Professional\Common7\Tools\VsDevCmd.bat",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2026\Enterprise\Common7\Tools\VsDevCmd.bat",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat",
        "$Env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat"
    )
    foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
    $found = Get-ChildItem "$Env:ProgramFiles(x86)\Microsoft Visual Studio" -Recurse -Filter VsDevCmd.bat -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($found) { return $found }
    throw "VsDevCmd.bat not found. Please install Visual Studio Desktop workloads."
}

$vsDev = Find-VsDevCmd
Write-Host "Using VS environment: $vsDev"

# Build via cmd so the VsDevCmd environment is applied
$projPath = Resolve-Path $Project
$cmd = "`"$vsDev`" && msbuild `"$projPath`" /p:Configuration=$Configuration /v:m"
Write-Host "Invoking: $cmd"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'cmd.exe'
$psi.Arguments = "/c $cmd"
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()

Write-Output $stdout
if ($stderr) { Write-Error $stderr }
if ($proc.ExitCode -ne 0) { throw "Build failed with exit code $($proc.ExitCode)" }

Write-Host "Build succeeded."
