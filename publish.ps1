$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "GitHubTopicReports.csproj"
$outputDirectory = Join-Path $projectRoot "dist"

dotnet restore $projectFile -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

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
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$nativeDirectory = Join-Path $outputDirectory "runtimes\win-x64\native"
$requiredNativeFiles = @(
    "avx2\llama.dll",
    "avx2\ggml.dll",
    "avx2\ggml-base.dll",
    "avx2\ggml-cpu.dll",
    "noavx\llama.dll",
    "noavx\ggml.dll",
    "noavx\ggml-base.dll",
    "noavx\ggml-cpu.dll"
)

foreach ($relativePath in $requiredNativeFiles) {
    $filePath = Join-Path $nativeDirectory $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "发布目录缺少 LLamaSharp 原生运行库：$filePath"
    }
}

Write-Host "Published to: $outputDirectory"
Write-Host "LLamaSharp CPU runtime: $nativeDirectory"
