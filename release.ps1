[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Patch', 'Minor', 'Major')]
    [string]$Bump = 'Patch',
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$GitArguments)
    & git @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArguments -join ' ') failed." }
}

Push-Location $PSScriptRoot
try {
    $branch = & git symbolic-ref --short HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Check out a branch before preparing a release.' }
    $pending = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the working tree.' }
    if ($pending -and -not $PrepareOnly) {
        throw 'Commit your changes first, or use -PrepareOnly to include the version bump in your own commit.'
    }

    $projectPath = Join-Path $PSScriptRoot 'WindowsDesktopMover.csproj'
    $manifestPath = Join-Path $PSScriptRoot 'app.manifest'
    $projectText = [IO.File]::ReadAllText($projectPath)
    $manifestText = [IO.File]::ReadAllText($manifestPath)
    $project = [xml]$projectText
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
    if (-not $versionNode -or $versionNode.InnerText -notmatch '^\d+\.\d+\.\d+$') {
        throw 'The project must have a three-part VersionPrefix.'
    }
    $current = [version]$versionNode.InnerText
    $next = switch ($Bump) {
        'Major' { '{0}.0.0' -f ($current.Major + 1) }
        'Minor' { '{0}.{1}.0' -f $current.Major, ($current.Minor + 1) }
        'Patch' { '{0}.{1}.{2}' -f $current.Major, $current.Minor, ($current.Build + 1) }
    }
    $tag = "v$next"
    $manifest = [xml]$manifestText
    if ($manifest.assembly.assemblyIdentity.version -ne "$current.0") {
        throw 'The project and manifest versions disagree. Correct them before releasing.'
    }
    & git show-ref --verify --quiet "refs/tags/$tag"
    if ($LASTEXITCODE -eq 0) { throw "$tag already exists locally." }
    if ($LASTEXITCODE -ne 1) { throw 'Could not check existing release tags.' }

    $action = if ($PrepareOnly) { 'Update source and manifest versions' } else { 'Update versions, commit, tag, and push the release' }
    if (-not $PSCmdlet.ShouldProcess($tag, $action)) { return }

    $projectText = $projectText.Replace("<VersionPrefix>$current</VersionPrefix>", "<VersionPrefix>$next</VersionPrefix>")
    $manifestText = $manifestText.Replace("version=`"$current.0`"", "version=`"$next.0`"")
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($projectPath, $projectText, $utf8)
    [IO.File]::WriteAllText($manifestPath, $manifestText, $utf8)
    if ($PrepareOnly) {
        Write-Output "Prepared $tag. Commit the version files with your changes, then push the commit and tag to publish."
        return
    }

    Invoke-Git -GitArguments @('add', '--', 'WindowsDesktopMover.csproj', 'app.manifest')
    Invoke-Git -GitArguments @('commit', '-m', "Release $tag")
    Invoke-Git -GitArguments @('tag', '-a', $tag, '-m', "Release $tag")
    Invoke-Git -GitArguments @('push', '--atomic', 'origin', "HEAD:refs/heads/$branch", "refs/tags/$tag")
    Write-Output "Pushed $tag. GitHub Actions will build and publish the release."
}
finally {
    Pop-Location
}
