param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildDirectory = Join-Path $projectRoot 'build'
$distDirectory = Join-Path $projectRoot 'dist'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x C# compiler was not found.'
}

New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$applicationFileName = (-join [char[]](0x4E32, 0x53E3, 0x73AF, 0x56DE, 0x6D4B, 0x8BD5)) + '.exe'
$applicationPath = Join-Path $distDirectory $applicationFileName
$candidatePath = Join-Path $buildDirectory 'RsLoopTest.release.exe'
$manifestPath = Join-Path $projectRoot 'app.manifest'
$iconPath = Join-Path $projectRoot 'assets\app-icon.ico'
$applicationArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/win32manifest:$manifestPath",
    "/win32icon:$iconPath",
    "/out:$candidatePath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sourceFiles

& $compiler $applicationArguments
if ($LASTEXITCODE -ne 0) {
    throw "Application compilation failed with exit code $LASTEXITCODE."
}

if (-not $SkipTests) {
    $testPath = Join-Path $buildDirectory 'LoopCoreTests.exe'
    $testSources = @(
        (Join-Path $projectRoot 'src\PayloadCodec.cs'),
        (Join-Path $projectRoot 'src\FrameBuffer.cs'),
        (Join-Path $projectRoot 'src\SerialTiming.cs'),
        (Join-Path $projectRoot 'src\BaudRateOptions.cs'),
        (Join-Path $projectRoot 'src\LoopDataOptions.cs'),
        (Join-Path $projectRoot 'src\GeneratedPayload.cs'),
        (Join-Path $projectRoot 'src\PayloadGenerator.cs'),
        (Join-Path $projectRoot 'src\Crc32.cs'),
        (Join-Path $projectRoot 'src\LoopFrame.cs'),
        (Join-Path $projectRoot 'src\LoopFrameCodec.cs'),
        (Join-Path $projectRoot 'src\LoopFrameParser.cs'),
        (Join-Path $projectRoot 'src\TransportSettings.cs'),
        (Join-Path $projectRoot 'src\LoopTransport.cs'),
        (Join-Path $projectRoot 'src\LoopTestMode.cs'),
        (Join-Path $projectRoot 'src\LoopSnapshot.cs'),
        (Join-Path $projectRoot 'src\SerialLoopController.cs'),
        (Join-Path $projectRoot 'tests\LoopCoreTests.cs')
    )
    $testArguments = @(
        '/nologo',
        '/target:exe',
        '/optimize+',
        "/out:$testPath",
        '/reference:System.dll',
        '/reference:System.Core.dll'
    ) + $testSources
    & $compiler $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Test compilation failed with exit code $LASTEXITCODE."
    }
    & $testPath
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }
}

try {
    Copy-Item -LiteralPath $candidatePath -Destination $applicationPath -Force
    $deployedPath = $applicationPath
}
catch [System.IO.IOException] {
    $fallbackFileName = (-join [char[]](0x4E32, 0x53E3, 0x73AF, 0x56DE, 0x6D4B, 0x8BD5)) + '_v1.4.0.exe'
    $deployedPath = Join-Path $distDirectory $fallbackFileName
    Copy-Item -LiteralPath $candidatePath -Destination $deployedPath -Force
    Write-Warning 'The existing application is running. The new version was published with a versioned file name.'
}

$output = Get-Item -LiteralPath $deployedPath
Write-Host ('Built: {0} ({1:N0} bytes)' -f $output.FullName, $output.Length)
