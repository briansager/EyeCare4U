#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[string]$ExistingDirectory)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$root=Join-Path ([IO.Path]::GetTempPath()) 'EyeCare4U-Review'
if($ExistingDirectory){$directory=[IO.Path]::GetFullPath($ExistingDirectory)}
else{
    $directory=Join-Path $root ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    'Isolated EyeCare4U review data. Never owner production history.' | Set-Content -LiteralPath (Join-Path $directory '.eyecare4u-review')
}
if([IO.Path]::GetDirectoryName($directory) -ine [IO.Path]::GetFullPath($root) -or -not (Test-Path -LiteralPath (Join-Path $directory '.eyecare4u-review'))){throw 'Not a marked isolated review directory.'}
Write-Output "Isolated data directory (keep this path for restart): $directory"
# The normal product has no review controls. An explicit argument only accepts marked GUID children of the temp review root.
$process=Start-Process -FilePath $exe -ArgumentList @('--isolated-review',('"'+$directory+'"')) -WindowStyle Hidden -PassThru
Write-Output "Started published executable, process ID $($process.Id). Window title identifies isolated review."
