# Third-party notices

X1 Control is distributed with third-party components. The first-party distribution notice does not apply to the third-party source snapshots or binaries listed here. Full license texts are under `licenses/` or next to the corresponding source snapshot.

## LibreHardwareMonitorLib 0.9.6

Used unmodified under MPL-2.0. Copyright LibreHardwareMonitor and contributors; portions copyright Open Hardware Monitor contributors. Source: [tag v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6), commit `3d331e3370efb858411f19511373eff65a218701`. License text: `licenses/LibreHardwareMonitor-MPL-2.0.txt`.

The library embeds signed PawnIO modules from the official PawnIO.Modules 0.1.6 release. These modules are licensed under LGPL-2.1-or-later. Their corresponding source and `COPYING` are included in `third-party-sources/PawnIO.Modules-0.1.6/`.

## PawnIO installer and driver

`PawnIO_setup.exe` in the Windows release archive is the unmodified installer embedded in LibreHardwareMonitor v0.9.6. Its file version is 2.1.0.0 and its Authenticode signature was valid at packaging time. The PawnIO driver is GPL-2.0-or-later with the upstream special exception described in its source. Corresponding driver source and its PawnPP submodule are included under `third-party-sources/PawnIO-2.1.0/`, with `COPYING` and the upstream notices.

## Other dependencies

- HidSharp 2.6.4: Apache-2.0. License text: `licenses/HidSharp-Apache-2.0.txt`.
- DiskInfoToolkit 1.1.2: MPL-2.0. Source commit `25319eae5781e75bcf141e844ceab2afe94d40ea`.
- RAMSPDToolkit-NDD 1.4.2: MPL-2.0. Source commit `3b47b960e0830fef344624ad5e389675d5f0a1ce`.
- Bundled .NET 10 runtime and System.* components: see `licenses/dotnet-MIT.txt` and `licenses/dotnet-THIRD-PARTY-NOTICES.txt`.

No changes were made to these third-party components. The ADLX integration is an original minimal, read-only binding to AMD's published C ABI and does not redistribute AMD binaries or SDK implementation.

Hardware protocol facts were cross-checked against Handheld Companion (CC-BY-NC-SA-4.0) and Linux oxp-sensors v6.15 (GPL-2.0-or-later). X1 Control does not include their executable code.
