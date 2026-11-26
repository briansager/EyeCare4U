#requires -Version 7.2
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Portable packaging must run on Windows.'}
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repository
try {
    function Invoke-DotNet([string[]]$Arguments) {
        & dotnet @Arguments
        if($LASTEXITCODE -ne 0){throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)."}
    }
    [xml]$properties=Get-Content (Join-Path $repository 'Directory.Build.props')
    $version=[string]$properties.Project.PropertyGroup.Version
    if($version -notmatch '^\d+\.\d+\.\d+-rc\.\d+$'){throw 'A deliberate rc version is required.'}
    [xml]$project=Get-Content 'src/EyeCare4U.App/EyeCare4U.App.csproj'
    $runtime=[string]($project.Project.PropertyGroup | Where-Object RuntimeFrameworkVersion).RuntimeFrameworkVersion
    $sdk=(& dotnet --version).Trim()
    if($LASTEXITCODE -ne 0){throw 'SDK is unavailable.'}
    $revision=(& git rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0){throw 'Build from a Git checkout.'}
    $dirty=[bool](& git status --porcelain=v1)
    Invoke-DotNet @('restore','EyeCare4U.slnx','--locked-mode')
    Invoke-DotNet @('build','EyeCare4U.slnx','--configuration','Release','--no-restore','-p:ContinuousIntegrationBuild=true')
    Invoke-DotNet @('test','EyeCare4U.slnx','--configuration','Release','--no-build','--no-restore')
    $auditText=(& dotnet list EyeCare4U.slnx package --vulnerable --include-transitive --format json --no-restore) -join "`n"
    if($LASTEXITCODE -ne 0){throw 'Dependency audit failed.'}
    $audit=$auditText | ConvertFrom-Json
    if($audit.version -ne 1 -or $auditText -match '"vulnerabilities"\s*:' -or $auditText -match '"problems"\s*:' -or $auditText -match '"severity"\s*:') {
        throw 'Dependency audit reported vulnerabilities/problems or an unsupported format. Inspect dotnet list package before publishing.'
    }
    $artifactRoot=Join-Path $repository 'artifacts'
    $stage=Join-Path $artifactRoot ('packaging/'+[Guid]::NewGuid().ToString('N'))
    $publish=Join-Path $stage 'EyeCare4U'
    New-Item -ItemType Directory -Path $publish -Force | Out-Null
    Invoke-DotNet @('restore','src/EyeCare4U.App/EyeCare4U.App.csproj','--runtime','win-x64','--locked-mode','-p:SelfContained=true')
    Invoke-DotNet @('publish','src/EyeCare4U.App/EyeCare4U.App.csproj','--configuration','Release','--runtime','win-x64','--self-contained','true','--no-restore','--output',$publish,
        '-p:PublishTrimmed=false','-p:PublishAot=false','-p:PublishSingleFile=false','-p:DebugType=None','-p:DebugSymbols=false','-p:ContinuousIntegrationBuild=true',"-p:PathMap=$repository=/_/")
    function Copy-PackageText([string]$Source,[string]$Name) {
        $text=[IO.File]::ReadAllText((Join-Path $repository $Source)).Replace("`r`n","`n")
        [IO.File]::WriteAllText((Join-Path $publish $Name),$text,[Text.UTF8Encoding]::new($false))
    }
    Copy-PackageText 'docs/PORTABLE-README.md' 'README.txt'
    Copy-PackageText 'docs/RELEASE-NOTES-0.6.0-rc.1.md' 'RELEASE-NOTES.md'
    Copy-PackageText 'THIRD-PARTY-NOTICES.md' 'THIRD-PARTY-NOTICES.md'
    $licenses=Join-Path $publish 'licenses'
    New-Item -ItemType Directory -Path $licenses | Out-Null
    Get-ChildItem -LiteralPath 'third-party' -File | Copy-Item -Destination $licenses
    # Include notices supplied with the exact self-contained runtime packs, rather than SDK/test-tool files.
    $nuget=(& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*',''
    if($LASTEXITCODE -ne 0){throw 'Cannot locate restored runtime notices.'}
    Copy-Item -LiteralPath (Join-Path $nuget "microsoft.netcore.app.runtime.win-x64/$runtime/LICENSE.TXT") -Destination (Join-Path $licenses 'NET-runtime-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $nuget "microsoft.netcore.app.runtime.win-x64/$runtime/THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $licenses 'NET-runtime-THIRD-PARTY-NOTICES.txt')
    Copy-Item -LiteralPath (Join-Path $nuget "microsoft.windowsdesktop.app.runtime.win-x64/$runtime/LICENSE") -Destination (Join-Path $licenses 'WindowsDesktop-LICENSE.txt')
    $files=@(Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName)
    $manifest=[ordered]@{version=$version;runtimeIdentifier='win-x64';selfContained=$true;runtimeVersion=$runtime;sdk=$sdk;packagerPowerShell=$PSVersionTable.PSVersion.ToString();sourceCommit=$revision;workingTreeDirty=$dirty;files=@()}
    foreach($file in $files){
        $relative=[IO.Path]::GetRelativePath($publish,$file.FullName).Replace('\','/')
        if($relative -match '(^|/)(bin|obj|logs?|TestResults|\.git)(/|$)|\.(db(?:-.*)?|sqlite\d?(?:-(?:wal|shm|journal))?|log|pdb|pfx|p12|snk|user|tmp)$|(^|/)(\.env(?:\..*)?|secrets\.json|appsettings\.Local\.json)$'){
            throw "Unexpected package content: $relative"
        }
        $manifest.files+=[ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publish 'package-manifest.json') -Encoding utf8NoBOM
    $output=Join-Path $artifactRoot 'packages'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $zip=Join-Path $output "EyeCare4U-$version-win-x64.zip"
    # Delete only the exact known generated archive; never recursively remove a computed directory.
    if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
    $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
    try{
        foreach($file in Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName){
            $entryName='EyeCare4U/'+[IO.Path]::GetRelativePath($publish,$file.FullName).Replace('\','/')
            $entry=$archive.CreateEntry($entryName,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $source=[IO.File]::OpenRead($file.FullName); $target=$entry.Open()
            try{$source.CopyTo($target)}finally{$target.Dispose();$source.Dispose()}
        }
    }finally{$archive.Dispose()}
    $hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    & (Join-Path $PSScriptRoot 'Test-PortablePackage.ps1') -ZipPath $zip
    & (Join-Path $PSScriptRoot 'Test-PackageValidation.ps1') -ZipPath $zip
    Write-Output "Portable package: $zip"
    Write-Output "SHA-256: $hash"
    Write-Output "Publish staging directory: $publish"
}finally{Pop-Location}
