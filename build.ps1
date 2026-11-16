param(
    [string]$Version,
    [switch]$NoIncrement
)

$ErrorActionPreference = "Stop"

# Budgets from the verification plan: the installed executable and the installer download.
$ExeBudgetBytes = 150MB
$SetupBudgetBytes = 70MB

function RunDotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') failed"
    }
}

function Get-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) {
        return $candidate
    }

    return $null
}

function Write-SignedUpdateManifest {
    param(
        [string]$Version,
        [string]$SetupPath,
        [string]$ManifestPath,
        [string]$SignaturePath
    )

    $setupItem = Get-Item $SetupPath
    $setupHash = (Get-FileHash $SetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestJson = [ordered]@{
        schemaVersion = 1
        version = $Version
        tag = "v$Version"
        assetName = "DiskMonitor-Setup.exe"
        downloadUrl = "https://github.com/rockenrooster/DiskMonitor/releases/download/v$Version/DiskMonitor-Setup.exe"
        sha256 = $setupHash
        sizeBytes = $setupItem.Length
        createdUtc = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json -Compress

    [System.IO.File]::WriteAllText($ManifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

    $privateKey = $env:DISKMONITOR_UPDATE_SIGNING_PRIVATE_KEY_PEM
    if ([string]::IsNullOrWhiteSpace($privateKey) -and $env:DISKMONITOR_UPDATE_SIGNING_PRIVATE_KEY_PEM_FILE) {
        $privateKey = Get-Content $env:DISKMONITOR_UPDATE_SIGNING_PRIVATE_KEY_PEM_FILE -Raw
    }

    if ([string]::IsNullOrWhiteSpace($privateKey)) {
        Remove-Item $ManifestPath, $SignaturePath -Force -ErrorAction SilentlyContinue
        if ($env:GITHUB_ACTIONS -eq "true") {
            throw "DISKMONITOR_UPDATE_SIGNING_PRIVATE_KEY_PEM is required to sign update manifests."
        }

        Write-Warning "DISKMONITOR_UPDATE_SIGNING_PRIVATE_KEY_PEM is not set; skipped signed update manifest."
        return
    }

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($privateKey)
        $manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
        $signature = $rsa.SignData(
            $manifestBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [System.IO.File]::WriteAllBytes($SignaturePath, $signature)
    }
    finally {
        $rsa.Dispose()
    }
}

# THIRD-PARTY-NOTICES.md: the licence and notices of the .NET runtime packs the executable carries,
# taken from the packs themselves (never typed by hand).
function Write-ThirdPartyNotices {
    param(
        [string]$ProjectDir,
        [string]$Destination
    )

    $assets = Get-Content (Join-Path $ProjectDir "obj\project.assets.json") -Raw | ConvertFrom-Json
    $packagesPath = $assets.project.restore.packagesPath
    $wanted = @("Microsoft.NETCore.App.Runtime.win-x64", "Microsoft.WindowsDesktop.App.Runtime.win-x64")
    $packs = @()
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        foreach ($dependency in $framework.Value.downloadDependencies) {
            if ($wanted -contains $dependency.name) {
                $packVersion = (($dependency.version -replace '[\[\]\(\)\s]', '') -split ',')[0]
                $packs += [pscustomobject]@{
                    Name = $dependency.name
                    Version = $packVersion
                    Path = Join-Path $packagesPath (Join-Path $dependency.name.ToLowerInvariant() $packVersion)
                }
            }
        }
    }

    if ($packs.Count -ne $wanted.Count) {
        throw "Runtime packs not found in project.assets.json; publish self-contained first."
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine("# Third-party notices")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("Disk Monitor carries its own copy of the .NET runtime and the Windows Desktop runtime, so nothing")
    [void]$builder.AppendLine("else has to be installed. Their licences and third-party notices follow, as shipped in the runtime")
    [void]$builder.AppendLine("packs this build was published with.")
    foreach ($pack in $packs) {
        # The core pack ships LICENSE.TXT and THIRD-PARTY-NOTICES.TXT; the Windows Desktop pack ships LICENSE only.
        $licence = @("LICENSE.TXT", "LICENSE") | Where-Object { Test-Path (Join-Path $pack.Path $_) } | Select-Object -First 1
        if (!$licence) {
            throw "No licence file in $($pack.Path)"
        }

        $files = @($licence) + @("THIRD-PARTY-NOTICES.TXT" | Where-Object { Test-Path (Join-Path $pack.Path $_) })
        foreach ($file in $files) {
            $path = Join-Path $pack.Path $file
            [void]$builder.AppendLine()
            [void]$builder.AppendLine("## $($pack.Name) $($pack.Version): $file")
            [void]$builder.AppendLine()
            [void]$builder.AppendLine('```text')
            [void]$builder.AppendLine((Get-Content $path -Raw).TrimEnd())
            [void]$builder.AppendLine('```')
        }
    }

    [System.IO.File]::WriteAllText($Destination, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
}

$projectDir = Join-Path $PSScriptRoot "src\DiskMonitor"
$csprojPath = Join-Path $projectDir "DiskMonitor.csproj"
$innoScript = Join-Path $PSScriptRoot "installer\DiskMonitor.iss"
$content = Get-Content $csprojPath -Raw

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Version must be x.y.z.w"
    }
    $newVersion = $Version
}
elseif ($content -match '<AssemblyVersion>(\d+)\.(\d+)\.(\d+)\.(\d+)</AssemblyVersion>') {
    if ($NoIncrement) {
        $newVersion = "$($matches[1]).$($matches[2]).$($matches[3]).$($matches[4])"
    }
    else {
        $newVersion = "$($matches[1]).$($matches[2]).$($matches[3]).$([int]$matches[4] + 1)"
    }
}
else {
    throw "AssemblyVersion not found in $csprojPath"
}

$content = $content -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$newVersion</AssemblyVersion>"
$content = $content -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$newVersion</FileVersion>"
Set-Content $csprojPath -Value $content -NoNewline

$publishDir = Join-Path $projectDir "obj\script-publish"
$artifactDir = Join-Path $PSScriptRoot "artifacts"
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

RunDotnet clean $csprojPath -c Release
RunDotnet publish $csprojPath -c Release -r win-x64 --self-contained true -o $publishDir /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:EnableCompressionInSingleFile=false

# A self-contained single file: anything else in the folder means a dependency was added without
# updating the installer and the packaging plan.
$unexpected = @(Get-ChildItem $publishDir -Force | Where-Object { $_.Name -ne "DiskMonitor.exe" })
if ($unexpected.Count -gt 0) {
    throw "The publish output must be DiskMonitor.exe only; also found: $($unexpected.Name -join ', ')"
}

$noticesPath = Join-Path $publishDir "THIRD-PARTY-NOTICES.md"
Write-ThirdPartyNotices -ProjectDir $projectDir -Destination $noticesPath

$publishExe = Join-Path $publishDir "DiskMonitor.exe"
$artifactExe = Join-Path $artifactDir "DiskMonitor.exe"
$artifactSha = Join-Path $artifactDir "DiskMonitor.exe.sha256"
$setupArtifact = Join-Path $artifactDir "DiskMonitor-Setup.exe"
$setupSha = Join-Path $artifactDir "DiskMonitor-Setup.exe.sha256"
$setupManifest = Join-Path $artifactDir "DiskMonitor-Setup.exe.manifest.json"
$setupManifestSig = Join-Path $artifactDir "DiskMonitor-Setup.exe.manifest.sig"

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
Remove-Item $setupManifest, $setupManifestSig -Force -ErrorAction SilentlyContinue
try {
    Copy-Item $publishExe $artifactExe -Force -ErrorAction Stop
}
catch {
    throw "Could not replace $artifactExe. Close any running DiskMonitor.exe from artifacts, then retry."
}

$hash = (Get-FileHash $artifactExe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  DiskMonitor.exe" | Set-Content $artifactSha

$iscc = Get-InnoCompiler
if ($iscc) {
    & $iscc "/DAppVersion=$newVersion" "/DSourceDir=$publishDir" "/DOutputDir=$artifactDir" $innoScript
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC failed"
    }

    $setupHash = (Get-FileHash $setupArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$setupHash  DiskMonitor-Setup.exe" | Set-Content $setupSha
    Write-SignedUpdateManifest -Version $newVersion -SetupPath $setupArtifact -ManifestPath $setupManifest -SignaturePath $setupManifestSig
}
elseif ($env:GITHUB_ACTIONS -eq "true") {
    throw "Inno Setup 6 is required to build DiskMonitor-Setup.exe."
}
else {
    Write-Warning "Inno Setup 6 not found; skipped DiskMonitor-Setup.exe."
    Remove-Item $setupArtifact, $setupSha, $setupManifest, $setupManifestSig -Force -ErrorAction SilentlyContinue
}

# Sizes against the budgets.
$exeSize = (Get-Item $artifactExe).Length
Write-Host ("DiskMonitor.exe: {0:N1} MB (budget {1:N0} MB)" -f ($exeSize / 1MB), ($ExeBudgetBytes / 1MB))
if ($exeSize -gt $ExeBudgetBytes) {
    throw "DiskMonitor.exe is over its size budget."
}

if (Test-Path $setupArtifact) {
    $setupSize = (Get-Item $setupArtifact).Length
    Write-Host ("DiskMonitor-Setup.exe: {0:N1} MB (budget {1:N0} MB)" -f ($setupSize / 1MB), ($SetupBudgetBytes / 1MB))
    if ($setupSize -gt $SetupBudgetBytes) {
        throw "DiskMonitor-Setup.exe is over its size budget."
    }
}

Write-Host "Published $artifactExe ($newVersion)" -ForegroundColor Green
