# RenText publish script
# Usage: .\publish.ps1

$projectFile = "RenText.csproj"
$publishDir  = "publish"

# Get version from csproj
[xml]$csproj  = Get-Content $projectFile
$version      = $csproj.Project.PropertyGroup.Version
$zipName      = "RenText-v$version-win-x64.zip"
$buildDir     = "$publishDir\_build"
$zipPath      = "$publishDir\$zipName"

Write-Host "Building RenText v$version..." -ForegroundColor Cyan

# Clean previous build
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
if (Test-Path $zipPath)  { Remove-Item $zipPath  -Force }

# Publish
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $buildDir
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

# Remove PDB files and runtime-generated files
Get-ChildItem $buildDir -Filter "*.pdb" | Remove-Item -Force
if (Test-Path "$buildDir\settings.json") { Remove-Item "$buildDir\settings.json" -Force }

# Create zip
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
Compress-Archive -Path "$buildDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

# Clean build dir
Remove-Item $buildDir -Recurse -Force

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Done: $zipPath ($sizeMB MB)" -ForegroundColor Green
