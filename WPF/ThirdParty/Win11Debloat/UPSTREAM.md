# Win11Debloat provenance

This directory vendors the runtime tree from [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat), commit `6012b02ea282f23ea943946206762fd430025c6f`.

The upstream project is licensed under MIT; its unmodified `LICENSE` is retained beside this file. MiniApps packages this maintained fork only in the .NET Framework 4.8 output as `Engine/Debloat`. It does not launch the upstream GUI: MiniApps makes a private working copy and invokes the entry point in its non-interactive host mode.

The fork adds a `-MiniApps` host contract: an explicit durable backup path, a reporting adapter path, a bounded Appx operation timeout, and per-package activity reporting. At run time MiniApps removes only `CreateRestorePoint` from a copied `Config/DefaultSettings.json`. Integration changes in this tree must remain narrowly scoped and must be reviewed again whenever the pinned upstream commit changes.

The runtime tree intentionally includes `Assets`, `Config`, `Regfiles`, `Schemas`, and `Scripts`. Although MiniApps runs the CLI default mode, the pinned entry point validates and dot-sources these dependencies before dispatching parameters.
