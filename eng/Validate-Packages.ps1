param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $BaselinePath = "$PSScriptRoot/PublicApiBaseline.txt",

    [switch] $UpdateBaseline
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedPackages = @(
    "EventStoreCore",
    "EventStoreCore.Abstractions",
    "EventStoreCore.CloudEvents",
    "EventStoreCore.Endpoints",
    "EventStoreCore.EventGrid",
    "EventStoreCore.Hangfire",
    "EventStoreCore.MassTransit",
    "EventStoreCore.Postgres",
    "EventStoreCore.Quartz",
    "EventStoreCore.Scheduling",
    "EventStoreCore.SDK",
    "EventStoreCore.SqlServer",
    "EventStoreCore.Testing",
    "EventStoreCore.TickerQ"
)

$resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter "*.nupkg"
$publicApi = [System.Collections.Generic.List[string]]::new()

foreach ($expectedPackage in $expectedPackages) {
    $matches = @($packages | Where-Object {
        $_.BaseName -match "^$([Regex]::Escape($expectedPackage))\.\d"
    })

    if ($matches.Count -ne 1) {
        throw "Expected exactly one package for '$expectedPackage', found $($matches.Count)."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($matches[0].FullName)
    try {
        $entries = @($archive.Entries.FullName)
        $assemblyPath = "lib/net10.0/$expectedPackage.dll"
        $documentationPath = "lib/net10.0/$expectedPackage.xml"

        if ($entries -notcontains $assemblyPath) {
            throw "Package '$expectedPackage' is missing '$assemblyPath'."
        }

        if ($entries -notcontains $documentationPath) {
            throw "Package '$expectedPackage' is missing '$documentationPath'."
        }

        if (-not ($entries | Where-Object { $_ -like "*.nuspec" })) {
            throw "Package '$expectedPackage' is missing its nuspec metadata."
        }

        if ($entries -notcontains "README.md") {
            throw "Package '$expectedPackage' is missing its package README."
        }

        $documentationEntry = $archive.GetEntry($documentationPath)
        $documentationStream = $documentationEntry.Open()
        $documentationReader = [System.IO.StreamReader]::new($documentationStream)
        try {
            [xml] $documentation = $documentationReader.ReadToEnd()
            foreach ($member in $documentation.doc.members.member) {
                $publicApi.Add("$expectedPackage|$($member.name)")
            }
        }
        finally {
            $documentationReader.Dispose()
            $documentationStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

$publicApi = @($publicApi | Sort-Object -Unique)
$resolvedBaselinePath = [System.IO.Path]::GetFullPath($BaselinePath)

if ($UpdateBaseline) {
    [System.IO.File]::WriteAllLines($resolvedBaselinePath, $publicApi)
    Write-Host "Updated public API baseline with $($publicApi.Count) documented symbols."
}
else {
    if (-not (Test-Path -LiteralPath $resolvedBaselinePath)) {
        throw "Public API baseline '$resolvedBaselinePath' does not exist. Run with -UpdateBaseline after reviewing the package API."
    }

    $baseline = @(Get-Content -LiteralPath $resolvedBaselinePath | Where-Object { $_ })
    $difference = @(Compare-Object -ReferenceObject $baseline -DifferenceObject $publicApi)
    if ($difference.Count -gt 0) {
        $details = $difference |
            ForEach-Object {
                $change = if ($_.SideIndicator -eq "=>") { "added" } else { "removed" }
                "  $change $($_.InputObject)"
            }
        throw "Public API differs from the reviewed baseline:`n$($details -join [Environment]::NewLine)"
    }
}

Write-Host "Validated $($expectedPackages.Count) package assemblies, XML documentation files, READMEs, nuspec files, and the public API baseline."
