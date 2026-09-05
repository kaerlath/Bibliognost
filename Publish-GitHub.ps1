[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryUrl,

    [string]$CommitMessage = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$projectRoot = $PSScriptRoot
Set-Location -LiteralPath $projectRoot

if ($RepositoryUrl -notmatch '^(?:https://github\.com/|git@github\.com:)(?<owner>[^/ :]+)/(?<repo>[^/ ]+?)(?:\.git)?$') {
    throw "Use a GitHub repository URL such as https://github.com/YourName/Bibliognost.git"
}

$owner = $Matches.owner
$repository = $Matches.repo
$canonicalUrl = "https://github.com/$owner/$repository"
$rawBase = "https://raw.githubusercontent.com/$owner/$repository/main"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "Git is not installed or is not available in PowerShell." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET SDK is not installed or is not available in PowerShell." }
if (-not (git config user.name)) { throw "Git has no user name. Run: git config --global user.name 'Your Name'" }
if (-not (git config user.email)) { throw "Git has no email. Run: git config --global user.email 'you@example.com'" }

$forbidden = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    'github_pat_[A-Za-z0-9_]{20,}',
    'gh[pousr]_[A-Za-z0-9]{30,}',
    'AKIA[0-9A-Z]{16}'
)
$textFiles = Get-ChildItem -LiteralPath $projectRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](?:\.git|bin|obj)[\\/]' -and
    $_.Extension -in @('.cs', '.csproj', '.json', '.md', '.ps1', '.yml', '.yaml', '.txt')
}
foreach ($file in $textFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($pattern in $forbidden) {
        if ($content -match $pattern) { throw "Possible credential found in $($file.FullName). Publication stopped." }
    }
}

$projectFile = Join-Path $projectRoot 'Bibliognost.csproj'
[xml]$projectXml = Get-Content -Raw -LiteralPath $projectFile
$propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1
$repoNode = $propertyGroup.SelectSingleNode('RepoUrl')
if ($null -eq $repoNode) {
    $repoNode = $projectXml.CreateElement('RepoUrl')
    $repoNode.InnerText = $canonicalUrl
    [void]$propertyGroup.AppendChild($repoNode)
} else {
    $repoNode.InnerText = $canonicalUrl
}
$projectXml.Save($projectFile)

dotnet restore --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Dependency restore failed." }
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

$builtDirectory = Join-Path $projectRoot 'bin\Release\Bibliognost'
$builtZip = Join-Path $builtDirectory 'latest.zip'
$builtManifest = Join-Path $builtDirectory 'Bibliognost.json'
if (-not (Test-Path -LiteralPath $builtZip) -or -not (Test-Path -LiteralPath $builtManifest)) {
    throw "The release build did not produce the expected package and manifest."
}
Copy-Item -LiteralPath $builtZip -Destination (Join-Path $projectRoot 'latest.zip') -Force

$manifest = Get-Content -Raw -LiteralPath $builtManifest | ConvertFrom-Json
$version = ([string]$manifest.AssemblyVersion) -replace '\.0$', ''
$manifest | Add-Member -NotePropertyName RepoUrl -NotePropertyValue $canonicalUrl -Force
$manifest | Add-Member -NotePropertyName IconUrl -NotePropertyValue "$rawBase/Assets/Branding/Bibliognost-Icon.png?v=$version" -Force
$manifest | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue "$rawBase/latest.zip" -Force
$manifest | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue "$rawBase/latest.zip" -Force
$manifest | Add-Member -NotePropertyName DownloadLinkTesting -NotePropertyValue "$rawBase/latest.zip" -Force
$manifestJson = $manifest | ConvertTo-Json -Depth 12
"[$manifestJson]" | Set-Content -LiteralPath (Join-Path $projectRoot 'repo.json') -Encoding utf8
if ([string]::IsNullOrWhiteSpace($CommitMessage)) { $CommitMessage = "Release Bibliognost $version" }

$readmePath = Join-Path $projectRoot 'README.md'
$readme = Get-Content -Raw -LiteralPath $readmePath
$customRepoUrl = "$rawBase/repo.json"
$readme = $readme -replace 'The exact custom-repository URL will appear here after `Publish-GitHub\.ps1` is run for the first time\.', "Custom repository URL: ``$customRepoUrl``"
$readme = $readme -replace 'Custom repository URL: `https://raw\.githubusercontent\.com/[^`]+/repo\.json`', "Custom repository URL: ``$customRepoUrl``"
Set-Content -LiteralPath $readmePath -Value $readme -Encoding utf8

$remoteExists = (git remote) -contains 'origin'
if ($remoteExists) { git remote set-url origin $RepositoryUrl } else { git remote add origin $RepositoryUrl }
if ($LASTEXITCODE -ne 0) { throw "Could not configure the GitHub remote." }

git add --all
$staged = git diff --cached --name-only
if (-not $staged) { throw "There are no changes to publish." }
git commit -m $CommitMessage
if ($LASTEXITCODE -ne 0) { throw "Git could not create the release commit." }
git branch -M main

$tag = "v$version"
if (git tag --list $tag) { throw "Tag $tag already exists. Increase the project version before publishing another release." }
git tag -a $tag -m "Bibliognost $version"

git push -u origin main
if ($LASTEXITCODE -ne 0) { throw "The main branch could not be pushed. After restoring network access, run: git push -u origin main --follow-tags" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "The release tag could not be pushed." }

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $projectRoot 'latest.zip')).Hash
Write-Host "Published Bibliognost $version to $canonicalUrl" -ForegroundColor Green
Write-Host "Dalamud repository: $customRepoUrl"
Write-Host "Release SHA-256: $hash"
