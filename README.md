# X1 Control

Windows monitor and fan-control utility for **ONEXPLAYER X1 A with Ryzen 7 8840U**.

## Download

Open the repository's **Releases** page and download `X1Control-Windows-x64.zip`. The archive includes the self-contained application, optional PawnIO installer, usage guide, third-party notices, and matching source package. The official unmodified binary is free to download and use.

## Features

- CPU/GPU temperatures, clocks and loads; APU package power; fan RPM and output; system RAM; battery level.
- Live temperature timeline and CPU/GPU/RAM usage bars.
- Firmware automatic, quiet, balanced, boost, manual and custom fan-curve modes.
- CSV history, sensor diagnostics and tray controls.

The RAM card reads system RAM sensors and keeps them separate from GPU VRAM. It shows installed RAM, the capacity reported by the system RAM sensors, current use, free memory, and a usage bar.

## Install and run

1. Extract the **entire** ZIP archive.
2. Run `X1Control.exe`. Readable sensors appear immediately.
3. For privileged CPU sensors and X1 fan control, install PawnIO from the app, then restart the app as administrator.
4. Close OneXConsole and Handheld Companion before using fan control; do not run multiple EC fan controllers together.

The app starts in the device's firmware automatic fan mode. Sensor availability depends on the Windows build, BIOS/EC version and driver state. See [docs/사용법.md](docs/사용법.md) for setup and recovery details.

## Build from source

Requirements: Windows x64 and the .NET 10 SDK. Package restore needs internet access.

```powershell
cd src
.\build.ps1
```

The self-contained executable is written to `src/publish/X1Control.exe`. Keep the official PawnIO installer and third-party notices with any release archive.

## Project terms

X1 Control's first-party source is published for inspection and is **not offered under an open-source license**. Official unmodified release binaries are free to download and use. See [DISTRIBUTION-NOTICE.md](DISTRIBUTION-NOTICE.md) for the exact terms and the limited rights required by bundled LGPL components. Third-party components retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

X1 Control is an independent project and is not affiliated with or endorsed by ONE-NETBOOK or ONEXPLAYER.
