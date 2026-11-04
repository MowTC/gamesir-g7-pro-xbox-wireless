param([switch]$Package)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    $wpf = Join-Path $framework 'WPF'
    if (!(Test-Path $compiler)) { throw 'Build on Windows x64 with .NET Framework 4.8.' }
    New-Item -ItemType Directory -Force bin | Out-Null
    $core = @('src\FirmwarePackage.cs', 'src\ReceiverBackup.cs')
    $refs = @('/r:System.Management.dll', '/r:System.Runtime.Serialization.dll', '/r:System.IO.Compression.dll')
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:bin\G7ProXboxWireless.exe /win32manifest:src\app.manifest `
      /resource:src\MainWindow.xaml,MainWindow.xaml "/r:$wpf\PresentationFramework.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\WindowsBase.dll" /r:System.Xaml.dll @refs @core src\MainWindow.cs
    if ($LASTEXITCODE) { throw 'App build failed.' }
    & $compiler /nologo /platform:x64 /optimize+ /out:bin\CoreTests.exe @refs @core tests\CoreTests.cs
    if ($LASTEXITCODE) { throw 'Test build failed.' }
    & .\bin\CoreTests.exe
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    if ($Package) {
        New-Item -ItemType Directory -Force dist | Out-Null
        $release = @('bin\G7ProXboxWireless.exe', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md')
        Compress-Archive -LiteralPath $release -DestinationPath dist\G7ProXboxWireless-0.1.1-win-x64.zip -Force
        $source = @('src', 'tests', 'docs', '.github', 'build.ps1', 'G7ProXboxWireless.csproj', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', '.gitignore')
        Compress-Archive -LiteralPath $source -DestinationPath dist\G7ProXboxWireless-0.1.1-source.zip -Force
        Get-FileHash dist\*.zip -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + (Split-Path $_.Path -Leaf) } | Set-Content dist\SHA256SUMS.txt
    }
} finally { Pop-Location }
