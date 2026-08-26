param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$workspacePath = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $workspacePath "WindowsDeskNote.sln"
$publishPath = Join-Path $workspacePath "artifacts\publish\$Runtime"
$selfContainedValue = $SelfContained.IsPresent.ToString().ToLowerInvariant()

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $solutionPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $workspacePath "src\DeskNote.App\DeskNote.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $selfContainedValue `
    --output $publishPath
exit $LASTEXITCODE
