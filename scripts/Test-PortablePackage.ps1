#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ZipPath)
$ErrorActionPreference='Stop'
$zip=[IO.Path]::GetFullPath($ZipPath)
$expected=(Get-Content -LiteralPath "$zip.sha256" -Raw).Split(' ',[StringSplitOptions]::RemoveEmptyEntries)[0]
if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ine $expected){throw 'ZIP checksum does not match.'}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    $entries=@{}; foreach($entry in $archive.Entries){
        if($entries.ContainsKey($entry.FullName) -or $entry.FullName -match '\.\.|^/|\\|\.(db(?:-.*)?|sqlite\d?(?:-(?:wal|shm|journal))?|log|pdb|pfx|user|tmp)$|(^|/)(\.env|secrets\.json|bin|obj|TestResults|\.git)(/|$)'){
            throw "Unsafe/duplicate archive entry: $($entry.FullName)"
        }
        $entries[$entry.FullName]=$entry
    }
    function Read-ZipText([string]$Name){
        if(-not $entries.ContainsKey($Name)){throw "Missing package entry: $Name"}
        $reader=[IO.StreamReader]::new($entries[$Name].Open());try{$reader.ReadToEnd()}finally{$reader.Dispose()}
    }
    $manifest=Read-ZipText 'EyeCare4U/package-manifest.json' | ConvertFrom-Json
    if(-not $manifest.selfContained -or $manifest.runtimeIdentifier -ne 'win-x64'){throw 'Unexpected package target.'}
    foreach($required in @('EyeCare4U.App.exe','EyeCare4U.App.dll','EyeCare4U.Storage.dll','e_sqlite3.dll','coreclr.dll','hostfxr.dll','PresentationFramework.dll','README.txt','THIRD-PARTY-NOTICES.md',
        'licenses/SQLitePCLRaw-LICENSE.txt','licenses/SQLitePCLRaw-NOTICE.txt','licenses/NET-runtime-LICENSE.txt','licenses/NET-runtime-THIRD-PARTY-NOTICES.txt','licenses/WindowsDesktop-LICENSE.txt',
        'licenses/Microsoft.Data.Sqlite-LICENSE.txt','licenses/SQLite-LICENSE.txt','licenses/WPF-THIRD-PARTY-NOTICES.txt','licenses/WindowsForms-THIRD-PARTY-NOTICES.txt')){
        if(-not $entries.ContainsKey("EyeCare4U/$required")){throw "Missing required component/notice: $required"}
    }
    $configuration=Read-ZipText 'EyeCare4U/EyeCare4U.App.runtimeconfig.json' | ConvertFrom-Json
    if($configuration.runtimeOptions.PSObject.Properties['frameworks'] -or $configuration.runtimeOptions.PSObject.Properties['framework']){throw 'Package still requires an installed shared framework.'}
    if(-not $configuration.runtimeOptions.includedFrameworks){throw 'Self-contained framework metadata is missing.'}
    $seen=@{}
    foreach($file in $manifest.files){
        $name="EyeCare4U/$($file.path)"
        if($seen.ContainsKey($name) -or -not $entries.ContainsKey($name)){throw "Invalid manifest entry: $name"}; $seen[$name]=$true
        $stream=$entries[$name].Open()
        try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
        if($actual -ne $file.sha256){throw "Manifest checksum mismatch: $name"}
    }
    if($seen.Count+1 -ne $entries.Count){throw 'Unmanifested package content.'}
    Write-Output "Package validated: $($entries.Count) files, self-contained win-x64, file manifest and ZIP checksums match."
}finally{$archive.Dispose()}
