# Local disk hygiene — MK-995

Local Windows builds in this checkout fail before compilation below 10 GiB free and warn below 20 GiB. This is a safety margin, not a guarantee that a large build fits. CI, design-time builds and Linux deployments are unchanged. Nested checkouts also inherit the guard when they do not have a nearer `Directory.Build.targets`. Worktrees outside this tree do not inherit it: merge the helpers there before relying on the guard. Use the wrapper to check space before restore as well.

Prefer the helper for local .NET builds/tests. It checks disk space before restore, uses one configuration (Release by default), and selects the host Windows runtime instead of copying native binaries for every platform into each project output. When this `Directory.Build.targets` is imported, `LocalDiskOptimizedBuild=true` also omits only `libSkiaSharp.pdb`, the large native C++ debug symbols. Managed PDBs, native DLLs and coverage remain available. For native Skia debugging, build directly without this flag. It does not change package versions, coverage rules or production publishing.

```powershell
./scripts/Invoke-LocalDotNet.ps1 -Command test -Project ./tests/Infrastructure.Images.UnitTests/Infrastructure.Images.UnitTests.csproj
./scripts/Invoke-LocalDotNet.ps1 -Command build -Project ./src/Api/Api.csproj
```

After a PR is merged and the worktree is no longer running, preview its regenerable outputs, then apply explicitly:

```powershell
./scripts/Clear-CompletedBuilds.ps1 -Worktree C:/path/to/completed/backend
./scripts/Clear-CompletedBuilds.ps1 -Worktree C:/path/to/completed/backend -Apply
# For squash merges, verify the exact merged PR head instead of Git ancestry:
./scripts/Clear-CompletedBuilds.ps1 -Worktree C:/path/to/completed/backend -MergedPullRequest 122 -Apply
```

Only immediate `bin` and `obj` directories of .NET projects under `src` and `tests` are eligible. Dirty/unmerged worktrees, tracked outputs, nested Git checkouts and filesystem links are refused. Processes referencing the absolute worktree path are checked on Windows, but that cannot detect every process launched with relative paths: stop the target runtime/build first. Reports and nested worktrees under `TestResults` are deliberately not age-deleted, since this project stores source checkouts there. Cleanup frees outputs, not the worktrees themselves; the next restore/build regenerates them.

Use PowerShell 7 for the wrapper and cleanup helper. The disk guard also supports Windows PowerShell 5.1 used by MSBuild.

CI runs the deterministic scenarios through Pester 5.7.1 on Windows and Linux.
All three production scripts remain in coverage; the test runner is classified
as test code. Windows must exercise every instrumented command. The quality job
merges real platform coverage, requires 100% covered lines, and imports that
report into SonarQube without lowering the existing gates. To run locally:

```powershell
Install-Module Pester -RequiredVersion 5.7.1 -Scope CurrentUser -Repository PSGallery -Force
./tests/Operations.LocalDiskTests/Invoke-Coverage.ps1
```

Docker is a separate source of growth. Never use volume pruning or Docker factory reset for disk housekeeping: local PostgreSQL/uploads may be lost. Check the existing Docker Engine settings first; an enabled `builder.gc` with a small `defaultKeepStorage` does not cap images, volumes, or the WSL disk. For the default Docker builder, merge the following into the existing Docker Engine JSON in Docker Desktop (do not replace unrelated settings or increase a smaller existing budget):

```json
{
  "builder": {
    "gc": {
      "enabled": true,
      "defaultKeepStorage": "10GB"
    }
  }
}
```

This is a cache-retention target, not a hard disk-size limit, and other buildx drivers need their own BuildKit configuration. Applying Docker Engine changes restarts Docker and interrupts local services; coordinate it first. Removing cache may not immediately shrink the Windows WSL virtual disk. Do not stop WSL or compact the disk while Docker is running.

References: [Docker garbage collection](https://docs.docker.com/build/cache/garbage-collection/), [.NET test runtime selection](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest).
