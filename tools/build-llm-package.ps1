param(
    [Parameter(Mandatory = $true)]
    [string] $ModelPath,

    [Parameter(Mandatory = $true)]
    [string] $LicensePath,

    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$model = Get-Item -LiteralPath $ModelPath
if ($model.Extension -ne ".gguf") {
    throw "模型文件必须是 .gguf 格式。"
}

$license = Get-Item -LiteralPath $LicensePath
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $model.DirectoryName "Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm"
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($output)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$modelSha256 = (Get-FileHash -LiteralPath $model.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    format = "LocalGithubLibrary.LLMPackage"
    formatVersion = 1
    packageId = "qwen2.5-1.5b-instruct-q4km"
    displayName = "Qwen2.5 1.5B Instruct Q4"
    modelFile = "models/qwen2.5-1.5b-instruct-q4_k_m.gguf"
    modelSha256 = $modelSha256
    quantization = "Q4_K_M"
    contextSize = 4096
    recommendedThreads = [Math]::Clamp([Environment]::ProcessorCount - 1, 2, 8)
    minimumAppVersion = "0.2.0"
    license = "Apache-2.0"
}

$manifestBytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 5))
$temporaryOutput = "$output.tmp"
if (Test-Path -LiteralPath $temporaryOutput) {
    Remove-Item -LiteralPath $temporaryOutput -Force
}

$stream = [IO.File]::Open($temporaryOutput, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)

try {
    $manifestEntry = $archive.CreateEntry("manifest.json", [IO.Compression.CompressionLevel]::Optimal)
    $manifestStream = $manifestEntry.Open()
    try {
        $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
    }
    finally {
        $manifestStream.Dispose()
    }

    $modelEntry = $archive.CreateEntry($manifest.modelFile, [IO.Compression.CompressionLevel]::NoCompression)
    $modelOutput = $modelEntry.Open()
    $modelInput = [IO.File]::OpenRead($model.FullName)
    try {
        $modelInput.CopyTo($modelOutput)
    }
    finally {
        $modelInput.Dispose()
        $modelOutput.Dispose()
    }

    $licenseEntry = $archive.CreateEntry("licenses/Qwen-LICENSE.txt", [IO.Compression.CompressionLevel]::Optimal)
    $licenseOutput = $licenseEntry.Open()
    $licenseInput = [IO.File]::OpenRead($license.FullName)
    try {
        $licenseInput.CopyTo($licenseOutput)
    }
    finally {
        $licenseInput.Dispose()
        $licenseOutput.Dispose()
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

Move-Item -LiteralPath $temporaryOutput -Destination $output
$package = Get-Item -LiteralPath $output
Write-Host "Created: $($package.FullName)"
Write-Host "Size: $([math]::Round($package.Length / 1GB, 2)) GB"
Write-Host "SHA256: $modelSha256"
