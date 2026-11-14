param([switch]$Test,[switch]$Publish)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)) {$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$names=@('DOTNET_CLI_HOME','NUGET_PACKAGES','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_ROOT','APPDATA')
$saved=@{}
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if($LASTEXITCODE){throw "dotnet failed ($LASTEXITCODE): $($Arguments[0])"}
}
try {
    $env:DOTNET_CLI_HOME=Join-Path $root '.tools\cli'
    $env:APPDATA=Join-Path $root '.tools\appdata'
    $env:NUGET_PACKAGES=Join-Path $root '.tools\packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
    $env:DOTNET_ROOT=Split-Path $dotnet
    $common=@('-m:1','-nr:false','-p:UseSharedCompilation=false','--nologo')
    Invoke-Dotnet (@('restore',"$root\DynamicIsland.sln",'--configfile',"$root\NuGet.Config",'--force','-m:1','-nr:false'))
    Invoke-Dotnet (@('build',"$root\DynamicIsland.sln",'-c','Release','--no-restore')+$common)
    if($Test){Invoke-Dotnet @("$root\modern\tests\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.Tests.dll","$root\artifacts\wpf-tests")}
    if($Publish){
        $preview=Join-Path $root 'artifacts\wpf-preview'
        Invoke-Dotnet (@('publish',"$root\modern\DynamicIsland.UI",'-c','Release','-r','win-x64','--self-contained','true','-o',"$preview\app",'--configfile',"$root\NuGet.Config")+$common)
        Invoke-Dotnet (@('publish',"$root\modern\DynamicIsland.BrowserHost",'-c','Release','-r','win-x64','--self-contained','true','-o',"$preview\browser-host",'--configfile',"$root\NuGet.Config")+$common)
        Copy-Item -LiteralPath "$root\modern\PREVIEW.md" -Destination "$preview\README.md" -Force
        Write-Host "Preview: $preview\app\Island.Wpf.exe"
    }
}
finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
