# MiniApps: WPF migration to .NET Framework 4.8

Status: implemented and locally verified on .NET Framework 4.8 x64. Clean Windows 10/11 VM verification and ARM64 remain outstanding; no GitHub release uploaded.

## Measured result (v0.2.0)

- MiniApps.exe: 125,440 bytes.
- Entire published directory: 1,231,939 bytes (1.175 MiB).
- ZIP: 468,713 bytes (0.447 MiB), 99.257% smaller than the 63,088,082-byte baseline.
- Release build: zero warnings/errors. 47 tests pass on net48 plus WPF dialog/navigation/render and install-preview integration checks.
- Bootstrap fixture and PowerShell syntax checks pass. NuGet vulnerability scan reports no vulnerable packages in the configured sources.
- System.Text.Json 8.0.6 retained for configuration compatibility; its transitive DLLs are included. No runtime bundled.
- Artifacts: `artifacts/publish-312e484c2b764351b62a846d61e4aa18/` and `artifacts/MiniApps-win-x64.zip`.
- SHA-256: `2f1e98cf3ff6bf8bc7129e51f3d7d37ef2ab81d7859dc119406d6835cf299b43`.

## Objective and baseline

- Preserve the WPF UI and current installer behavior while removing the bundled .NET 10 runtime.
- Current x64 release: ZIP 63,088,082 bytes (60.17 MiB); published directory approximately 139.53 MiB.
- Target compressed download: <= 5 MiB, with 0.5–3 MiB as an estimate pending a measured build. Installer downloads are excluded.
- Target normal Windows 10 21H2/build 19044 or newer and Windows 11 installations with .NET Framework 4.8 or newer present.
- Retain the current release as the comparison/rollback artifact. Do not overwrite existing user configuration or delete old artifacts during migration.

## 1. Establish a buildable net48 candidate

- Retarget MiniApps and MiniApps.Tests to net48; keep SDK-style WPF projects and explicitly select a compiler language version compatible with the existing source syntax.
- Use Microsoft.NETFramework.ReferenceAssemblies as a build-only dependency where needed. Do not ship reference assemblies or developer tools.
- First validate the installed SDK can compile net48 WPF markup. If it cannot, use Windows MSBuild/Visual Studio Build Tools for the build machine only.
- Prefer small compatibility helpers or ordinary DTO classes for records/init support rather than adding a broad compatibility package.
- Produce an isolated x64 candidate first. Preserve the architecture contract of the bootstrap; do not redirect ARM64/x86 to an untested binary.

## 2. Port runtime-dependent APIs

| Area | Files | Required work |
| --- | --- | --- |
| Process execution | DeploymentService.cs, DeviceInfoService.cs | Replace ArgumentList with correctly quoted Arguments; use EnvironmentVariables; implement asynchronous exit observation with Exited, race handling, and disposal. Drain stdout/stderr before completion. |
| Cancellation | DeploymentService.cs, DeviceInfoService.cs | Preserve running installers when stopping queued work. Keep CIM timeout bounded; any termination applies only to the owned diagnostic process. |
| Downloads | DeploymentService.cs | Use byte-array ReadAsync/WriteAsync overloads and ordinary using; retain streaming, cancellation, timeout, retries, and file validation. |
| Concurrency | DeploymentService.cs | Explicitly configure HTTP connection capacity for the catalog workload; retain concurrent downloads/EXE work, MSI serialization, and 1618 retry behavior. |
| Hashing | DeploymentService.cs | Replace HashDataAsync/ToHexString with streamed SHA256 computation and hex conversion; keep cancellation and avoid blocking the UI. |
| Settings | SettingsStore.cs | Replace overwrite File.Move with same-directory File.Replace for existing targets and File.Move for new targets; preserve the prior file on failure. |
| Collections/helpers | Models, services, tests | Replace unsupported BCL APIs and keep compiler-only syntax where supported; migrate equivalent tests instead of removing coverage. |

## 3. Keep JSON and settings compatible

- First candidate: use a pinned, supported System.Text.Json package compatible with net48; inspect its transitive output size and required binding redirects.
- Keep apps.json/windows.json schemas, casing rules, Unicode scripts, legacy arrays, removed-default IDs, and draft isolation compatible.
- If JSON dependencies push the package above the target, compare Newtonsoft.Json in an isolated candidate with configuration fixtures before choosing it. Do not change serializer purely on an assumed size saving.
- Do not add Optimize execution or change the selected debloat profile during this runtime migration.

## 4. Update publish and bootstrap

- Publish only MiniApps executable/configuration, necessary dependency DLLs, and required scripts. Exclude test output, PDBs, documentation, and reference assemblies from the release ZIP.
- Remove .NET 10 self-contained/single-file/runtime publishing switches. Update Preview.ps1/build documentation and the test invocation for net48.
- Keep ZIP, SHA-256 and versioned manifest delivery. Use a distinct version for the candidate release.
- Bootstrap detects .NET Framework via the registry Release value (4.8 or newer), including the appropriate registry view. Do not silently install a runtime.
- Keep Windows build checks, elevation, architecture routing, temporary session ownership, process waiting, and scoped cleanup.
- Verify cleanup still waits for the app and its workers. Account for ZIP plus extracted files and subsequent installer downloads when reporting temporary disk use.

## 5. Acceptance and handoff

- Run migrated service/configuration tests and WPF dispatcher/render tests on the actual net48 runtime.
- Verify all four pages, Office/WPS/Cancel, three-column installation progress, expandable Windows Setting, and navigation while busy (Setting locked).
- Optimize remains preview-only; Debloat remains excluded from Install.
- Use local HTTP/process fixtures to verify concurrent transfers, SHA-256 failure, timeouts, output draining, MSI gating, cancellation, and bootstrap cleanup without actual installation on the development machine.
- Verify clean Windows 10 and Windows 11 VMs without modern .NET Desktop Runtime. Actual installation/optimization checks belong in disposable VMs, not the development machine.
- Report EXE, whole publish directory, compressed ZIP, and dependency breakdown in exact bytes/MiB; compare against the 60.17 MiB baseline.
- Open the net48 preview for user review. Publish to GitHub only when release distribution is authorized. If VM infrastructure is unavailable, state that compatibility testing remains outstanding.

## Sources

- Windows/.NET Framework versions: https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements
- Build-time reference assemblies: https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/reference-assemblies
- Runtime detection: https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed
