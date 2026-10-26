$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "GitHubTopicReports.csproj"
$outputDirectory = Join-Path $projectRoot "dist"

dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputDirectory

Write-Host "Published to: $outputDirectory"
