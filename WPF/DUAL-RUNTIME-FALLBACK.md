# MiniApps dual-runtime fallback

Status: implemented and locally built for x64. GitHub upload and clean-VM certification remain outstanding.

## Runtime selection

- Windows builds below 17763 are rejected before any package download.
- .NET Framework Release `>= 528040` selects `net48`.
- A missing or lower Framework Release selects `net10`, published self-contained. MiniApps does not install a framework or shared .NET runtime.
- The current release supports `win-x64` only. x86 and ARM64 fail with a clear message.

The project multi-targets `net48;net10.0-windows` from the same C#/XAML source. The net48-only reference assemblies and System.Text.Json package are conditional; .NET 10 uses its shared compile-time framework and includes the Windows Desktop runtime in its published package.

## Release contract

`Publish.ps1` produces two ZIPs and checksums plus `manifest-win-x64.json` schema 2. The manifest contains one `net48` and one `net10` asset for the same version and architecture. Each entry records its immutable GitHub Release URL, SHA-256, byte size, and self-contained flag.

Bootstrap downloads the manifest first, validates its schema and exact selected entry, then downloads only that package. It accepts only the repository's HTTPS GitHub Release paths, checks the downloaded byte size and SHA-256, and then extracts and runs the app. Existing session ownership, process-tree wait, and cleanup rules remain in force.

The schema 1 manifest and old `MiniApps-win-x64.zip` are not silently accepted. Source bootstrap and all five schema 2 release assets must be published together to avoid mixing release contracts.

## Verification boundary

Both targets compile and share the existing behavior tests. Bootstrap tests cover build 17762/17763, missing/lower/equal/higher Framework Release selection, both valid target entries, malformed hashes, missing assets, local package execution, child-process waiting, and cleanup without running installers or Windows settings.

.NET 10 support on Windows 10 1809 depends on the applicable Microsoft-supported edition and servicing state; 1809 editions outside that matrix are not certified by this build. A clean Windows 10 1809 VM without Framework 4.8 and without a shared .NET runtime must still validate the self-contained package before customer release. Windows 11 and supported Windows 10 editions also require release-candidate VM coverage.
