# NugetSync

NugetSync scans a repository for NuGet packages, applies centralized rules, and emits an Excel-friendly TSV report.

## Quick start

1) Initialize centralized storage:

```
dotnet run --project src/NugetSync.Cli -- init --data-root "D:\NugetSyncData"
```

2) Run in any repo root:

```
dotnet run --project src/NugetSync.Cli -- run
```

## Use as a local .NET tool

1) Build or pack the tool (nupkg goes to `src/NugetSync.Cli/bin`):

```
dotnet build src/NugetSync.Cli -c Release
```

2) Create a tool manifest (once per repo):

```
dotnet new tool-manifest
```

3) Install the tool from the local nupkg:

```
dotnet tool install NugetSync.Cli --add-source src/NugetSync.Cli/bin
```

4) Run it:

```
dotnet dotnet-nugetsync --help
dotnet dotnet-nugetsync run
```

Outputs are written under:

```
<DataRoot>\outputs\<repoKey>\NugetSync.Report.tsv
<DataRoot>\outputs\<repoKey>\NugetSync.Inventory.json
```

## Rules file

Rules live at:

```
<DataRoot>\nugetsyncrules.json
```

Minimal example:

```json
{
  "schemaVersion": 1,
  "packages": [
    {
      "id": "Newtonsoft.Json",
      "action": "upgrade",
      "targetVersion": "13.0.3",
      "targetPolicy": "exact_or_higher",
      "upgrades": [
        {
          "from": "12.*",
          "to": "13.0.3",
          "notes": "Breaking: update usages."
        }
      ]
    }
  ]
}
```

## Report columns

The TSV report columns are:

```
ProjectUrl, RepoRef, CsprojPath, Frameworks, NugetName, Action, TargetVersion, Comment, DateUpdated
```

## Command reference

```
dotnet dotnet-nugetsync init --data-root <path>
dotnet dotnet-nugetsync run [--repo <path>] [--rules <path>] [--output <path>] [--inventory <path>] [--include-transitive true|false] [--force]
dotnet dotnet-nugetsync run-all [--force]
dotnet dotnet-nugetsync merge
dotnet dotnet-nugetsync list
dotnet dotnet-nugetsync update [--branch <name>]
dotnet dotnet-nugetsync interactive
dotnet dotnet-nugetsync rules add
dotnet dotnet-nugetsync rules add-mass
```

## Update repos

For all saved repository paths, if the working tree has no uncommitted changes, switch to the given branch (default: `develop`) and run `git pull`. Errors are logged during the run; at the end, all repos where the update failed are listed. Exit code is 1 if any repo failed.

```
dotnet dotnet-nugetsync update
dotnet dotnet-nugetsync update --branch main
```

## Interactive rule add

```
dotnet run --project src/NugetSync.Cli -- rules add
dotnet dotnet-nugetsync rules add
```

The wizard uses short numbered choices for predefined options.

## Mass add rules

Paste a list of package IDs and apply the same rule fields to all of them:

```
dotnet run --project src/NugetSync.Cli -- rules add-mass
dotnet dotnet-nugetsync rules add-mass
```

Enter one package ID per line and finish with `done`.

## Optional flags

```
--repo <path>
--rules <path>
--output <path>
--inventory <path>
--include-transitive true|false
--force
```

## Tests and coverage

Run tests:

```
dotnet test tests/NugetSync.Cli.Tests/NugetSync.Cli.Tests.csproj
```

Generate a coverage report (Cobertura XML):

```
dotnet test tests/NugetSync.Cli.Tests/NugetSync.Cli.Tests.csproj --collect "XPlat Code Coverage"
```

Coverage files are written under:

```
tests/NugetSync.Cli.Tests/TestResults/<run-id>/coverage.cobertura.xml
```

## Sample project

There is a sample project at `samples/SampleApp` with a couple of NuGet dependencies,
useful for testing the scanner quickly.

There is also a second sample at `samples/SampleApp2` with additional packages for
policy coverage (higher/lower/exact variations).

## Sample test scenarios

Sample rules covering remove/higher/lower/exact policies are available at:

```
samples/TestRules/nugetsyncrules.json
```

To try them quickly:

1) Initialize data root:
```
dotnet run --project src/NugetSync.Cli -- init --data-root "D:\NugetSyncData"
dotnet dotnet-nugetsync init --data-root "D:\NugetSyncData"
```
2) Copy the sample rules file to your data root:
```
copy samples\TestRules\nugetsyncrules.json "D:\NugetSyncData\nugetsyncrules.json"
```
3) Run in repo root:
```
dotnet run --project src/NugetSync.Cli -- run
dotnet dotnet-nugetsync run
```
