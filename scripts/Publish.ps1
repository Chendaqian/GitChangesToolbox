$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\GitChangesToolbox\GitChangesToolbox.csproj'
$projectDirectory = Join-Path $repoRoot 'src\GitChangesToolbox'
$publishDirectory = Join-Path $repoRoot 'publish'
$releaseOutputDirectory = Join-Path $projectDirectory 'bin\Release'
$releaseIntermediateDirectory = Join-Path $projectDirectory 'obj\Release'

$msbuildCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
)

$msbuildPath = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $msbuildPath) {
    throw '未找到 VS2022 MSBuild，请安装 Visual Studio 的 MSBuild 或修改脚本中的路径'
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "未找到项目文件: $projectPath"
}

Write-Host '开始构建 Git Changes Toolbox Release...' -ForegroundColor Cyan
foreach ($directory in @($releaseOutputDirectory, $releaseIntermediateDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

& $msbuildPath $projectPath /restore /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) {
    throw "构建失败，MSBuild 退出码: $LASTEXITCODE"
}

$vsixCandidates = @(
    (Join-Path $projectDirectory 'bin\Release\GitChangesToolbox.Probe.vsix'),
    (Join-Path $projectDirectory 'bin\Release\net472\GitChangesToolbox.Probe.vsix')
)
$vsixPath = $vsixCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $vsixPath) {
    throw "构建成功但未找到 VSIX 产物，已检查: $($vsixCandidates -join ', ')"
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
$publishedVsixPath = Join-Path $publishDirectory 'GitChangesToolbox.vsix'
Copy-Item -LiteralPath $vsixPath -Destination $publishedVsixPath -Force

Write-Host ''
Write-Host '发布完成:' -ForegroundColor Green
Write-Host $publishedVsixPath -ForegroundColor Green
Write-Host ''
if (-not [Console]::IsInputRedirected) {
    Read-Host '按 Enter 键退出'
}