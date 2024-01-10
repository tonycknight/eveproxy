param(
    [Parameter(Mandatory=$false)]
    [string]$Target = "All"
)
$ErrorActionPreference = 'Stop'

& dotnet tool restore

& dotnet run --project .\build\build.fsproj -t $Target
