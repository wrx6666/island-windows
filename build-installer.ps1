param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.11.0',
    [string]$GitHubRepository='',
    [string]$OutputDirectory='',
    [switch]$IntegrationTest
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if($GitHubRepository -and $GitHubRepository -notmatch '^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+/?$'){throw 'Use a public https://github.com/owner/repository URL.'}
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$vpk=Join-Path $root '.tools/velopack/vpk.exe'
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'release/modern'}
$stage=Join-Path $root ('artifacts/installer-stage-'+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $stage 'app'
$hostOutput=Join-Path $stage 'host'
$names=@('DOTNET_CLI_HOME','APPDATA','NUGET_PACKAGES','DOTNET_ROOT','DOTNET_CLI_TELEMETRY_OPTOUT')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
function Dotnet([string[]]$Arguments){& $dotnet @Arguments;if($LASTEXITCODE){throw "dotnet failed: $($Arguments[0])"}}
try{
    $env:DOTNET_CLI_HOME=Join-Path $root '.tools/cli';$env:APPDATA=Join-Path $root '.tools/appdata';$env:NUGET_PACKAGES=Join-Path $root '.tools/packages';$env:DOTNET_ROOT=Split-Path $dotnet;$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    if(!(Test-Path -LiteralPath $vpk)){Dotnet @('tool','install','vpk','--tool-path',(Split-Path $vpk),'--version','1.2.161','--configfile',"$root/NuGet.Config")}
    $common=@('-c','Release','-r','win-x64','--self-contained','true',"-p:Version=$Version",'-m:1','-nr:false','-p:UseSharedCompilation=false','--nologo')
    Dotnet (@('publish',"$root/modern/DynamicIsland.UI",'-o',$payload,'--configfile',"$root/NuGet.Config")+$common)
    # Publish the native host against the same Windows Desktop runtime. Mixing
    # the core runtime's WindowsBase facade with WPF's implementation is unsafe.
    Dotnet (@('publish',"$root/modern/DynamicIsland.BrowserHost",'-o',$hostOutput,'--configfile',"$root/NuGet.Config",'-p:IslandDistributionBuild=true')+$common)
    # The native host shares the exact self-contained .NET runtime with the WPF app.
    foreach($file in Get-ChildItem -LiteralPath $hostOutput -File){
        $target=Join-Path $payload $file.Name
        if(!(Test-Path -LiteralPath $target)){Copy-Item -LiteralPath $file.FullName -Destination $target}
        elseif((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash -and $file.Extension -ne '.pdb'){throw "Runtime payload conflict: $($file.Name)"}
    }
    $extension=Join-Path $payload 'browser-extension';[IO.Directory]::CreateDirectory($extension)|Out-Null
    foreach($name in @('manifest.json','background.js','content.js','page.js','popup.html','popup.js')){Copy-Item -LiteralPath (Join-Path $root "browser-extension/$name") -Destination (Join-Path $extension $name)}
    [IO.File]::WriteAllText((Join-Path $payload 'update-source.json'),(@{Repository=$GitHubRepository.TrimEnd('/');IntegrationTest=[bool]$IntegrationTest}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    # Keep the runtime's THIRD-PARTY-NOTICES.TXT intact (Windows ignores case).
    Copy-Item -LiteralPath "$root/distribution/THIRD-PARTY-NOTICES.txt" -Destination (Join-Path $payload 'ISLAND-THIRD-PARTY-NOTICES.txt')
    $licenses=Join-Path $payload 'Licenses';[IO.Directory]::CreateDirectory($licenses)|Out-Null
    $runtimeConfig=Get-Content -LiteralPath (Join-Path $payload 'Island.Wpf.runtimeconfig.json') -Raw|ConvertFrom-Json
    foreach($framework in $runtimeConfig.runtimeOptions.includedFrameworks){
        $package=$framework.name.ToLowerInvariant()+'.runtime.win-x64'
        $packageRoot=Join-Path $env:NUGET_PACKAGES ($package+'/'+$framework.version)
        $notices=@(Get-ChildItem -LiteralPath $packageRoot -File|Where-Object {$_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$'})
        if(!$notices){throw "Runtime license missing: $package"}
        foreach($notice in $notices){Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses ($framework.name+'-'+$notice.Name+'.txt'))}
    }
    # No local settings, profiles, caches, logs, tests or credentials are copied into the payload.
    $packId=if($IntegrationTest){'Island.Desktop.IntegrationTest'}else{'Island.Desktop'}
    $shortcuts=if($IntegrationTest){'None'}else{'Desktop,StartMenuRoot'}
    & $vpk pack --packId $packId --packVersion $Version --packDir $payload --mainExe Island.Wpf.exe --packTitle Island --packAuthors 'Island' --runtime win-x64 --outputDir $output --shortcuts $shortcuts --skip-updates --yes
    if($LASTEXITCODE){throw 'Velopack packaging failed'}
    if(!$IntegrationTest){Copy-Item -LiteralPath (Join-Path $output 'Island.Desktop-win-Setup.exe') -Destination (Join-Path $output 'IslandSetup.exe')}
    foreach($file in Get-ChildItem -LiteralPath $output -File | Where-Object {$_.Name -notlike '*.sha256'}){
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        [IO.File]::WriteAllText(($file.FullName+'.sha256'),$hash+'  '+$file.Name+"`n",[Text.UTF8Encoding]::new($false))
    }
    Write-Output "Installer and update feed: $output"
    if(!$GitHubRepository){Write-Warning 'Repository is not configured. This installer works offline; remote update discovery stays disabled until a repository is embedded in a new build.'}
}
finally{
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
    $absoluteStage=[IO.Path]::GetFullPath($stage);$allowed=[IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd('\')+'\'
    if(!$absoluteStage.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid staging cleanup path'}
    if(Test-Path -LiteralPath $absoluteStage){Remove-Item -LiteralPath $absoluteStage -Recurse -Force}
}
