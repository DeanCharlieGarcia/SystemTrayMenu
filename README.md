# HitNGone

HitNGone is a Windows tray application for browsing and opening files, folders and shortcuts through a searchable menu.

This project is derived from the GPL-3.0 SystemTrayMenu v2.x source by Markus Hofknecht and contributors. Original copyright notices and the [license](LICENSE) are preserved. The [original project documentation](UPSTREAM.md) describes the upstream application; its downloads, donations and support links refer to SystemTrayMenu, not HitNGone.

## Build and run

Install Visual Studio 2026 Community with **.NET desktop development** and .NET SDK **10.0.404**. In PowerShell:

```powershell
.\Build.ps1
.\Build.ps1 -Publish
.\artifacts\HitNGone\HitNGone.exe
```

The build runs the regression suite. Publishing creates a self-contained Windows x64 application in `artifacts\HitNGone`; copy the entire directory when distributing the app. No separate .NET runtime is required.

Use `HitNGone.sln` to open the project in Visual Studio. Store packaging remains a legacy, separately configured target and is not part of the portable build.

## Settings

HitNGone stores settings in `%APPDATA%\HitNGone`. On first use, existing SystemTrayMenu settings for this computer are copied and adapted if HitNGone has no configuration yet; the original file is retained. Portable `user.config` files also accept the old settings section. Existing configured folder and icon paths remain valid.

After moving the application, enable autostart and the Explorer context-menu entry again in settings if you use them, so their executable paths point to the new location.

See [DEVELOPMENT.md](DEVELOPMENT.md) for validation, diagnostics and outstanding work.
