#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ZipPath)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$folder=Join-Path $repository ('artifacts/package-tests/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$copy=Join-Path $folder 'candidate.zip'
function Set-TestChecksum {
    $hash=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  candidate.zip" | Set-Content -LiteralPath "$copy.sha256" -Encoding ascii
}
function Assert-Rejected([string]$Expected) {
    $rejected=$false
    try{& (Join-Path $PSScriptRoot 'Test-PortablePackage.ps1') -ZipPath $copy}
    catch{if($_.Exception.Message -notmatch $Expected){throw};$rejected=$true}
    if(-not $rejected){throw "Verifier accepted invalid package: $Expected"}
}
Copy-Item -LiteralPath $ZipPath -Destination $copy
('0'*64+'  candidate.zip') | Set-Content -LiteralPath "$copy.sha256" -Encoding ascii
Assert-Rejected 'ZIP checksum does not match'
# Changing a document while recomputing the outer checksum must still fail its inner manifest hash.
$archive=[IO.Compression.ZipFile]::Open($copy,[IO.Compression.ZipArchiveMode]::Update)
try{
    $archive.GetEntry('EyeCare4U/README.txt').Delete()
    $writer=[IO.StreamWriter]::new($archive.CreateEntry('EyeCare4U/README.txt').Open())
    try{$writer.Write('tampered test document')}finally{$writer.Dispose()}
}finally{$archive.Dispose()}
Set-TestChecksum
Assert-Rejected 'Manifest checksum mismatch'
# Isolated synthetic data, never owner data. Even a valid ZIP checksum cannot permit a database.
$archive=[IO.Compression.ZipFile]::Open($copy,[IO.Compression.ZipArchiveMode]::Update)
try{
    $writer=[IO.StreamWriter]::new($archive.CreateEntry('EyeCare4U/history.db').Open())
    try{$writer.Write('synthetic forbidden database')}finally{$writer.Dispose()}
}finally{$archive.Dispose()}
Set-TestChecksum
Assert-Rejected 'Unsafe/duplicate archive entry'
Write-Output 'Packaging regressions passed: wrong ZIP checksum, altered manifest content, and injected database were rejected.'
