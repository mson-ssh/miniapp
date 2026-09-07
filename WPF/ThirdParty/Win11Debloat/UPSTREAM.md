# Win11Debloat provenance

This directory vendors the runtime tree from [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat), commit `6012b02ea282f23ea943946206762fd430025c6f`.

The upstream project is licensed under MIT; its unmodified `LICENSE` is retained beside this file. MiniApps packages this tree only in the .NET Framework 4.8 output as `Engine/Debloat`. It does not launch the upstream GUI: MiniApps makes a private working copy and invokes `Win11Debloat.ps1 -RunDefaults -Silent -SkipExplorerRestart -LogPath <session-log>`.

MiniApps changes no vendored upstream source. At run time it removes only `CreateRestorePoint` from a copied `Config/DefaultSettings.json`, injects its task-reporting bridge into the copied entry point, and preserves the upstream Registry backup. Do not edit files in this directory to customize the MiniApps profile; keep integration changes under `MiniApps/Scripts/` so an upstream update can be reviewed as a clean vendor replacement.

The runtime tree intentionally includes `Assets`, `Config`, `Regfiles`, `Schemas`, and `Scripts`. Although MiniApps runs the CLI default mode, the pinned entry point validates and dot-sources these dependencies before dispatching parameters.
