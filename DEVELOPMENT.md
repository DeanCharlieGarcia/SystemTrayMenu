# HitNGone development

Original baseline: Hofknecht/SystemTrayMenu, branch v2.x, commit 259324280b9bcfc7c027abc23497b20d43271d15.

## Free local development tools

Visual Studio 2026 Community with .NET desktop development and .NET SDK 10.0.404.
`global.json` pins the .NET 10 SDK (latest patch within that feature band), avoiding automatic selection of a newer major SDK.
Verified installation: Visual Studio Community 2026 18.12.0. Application and regression runner target .NET 10 and remain self-contained Windows x64 builds; the Windows target version is unchanged.
The build uses SDK-provided .NET analyzers instead of the old explicit preview package. StyleCop remains at its existing version.

## Build and tests

From PowerShell in the repository root:

```powershell
.\Build.ps1
.\Build.ps1 -Benchmark
.\Build.ps1 -Publish
```

The default builds Release and runs all regression groups. `-Configuration Debug` builds Debug; `-SkipTests` skips execution. No third-party test packages are required. The build no longer kills running HitNGone processes. If an executable is locked, close that application and retry.

## Completed

### Stability

- IPC framing handles long, fragmented and incomplete messages and validates lengths.
- Pipe streams and connection wait handles are released.
- Menu results carry a unique request identity; worker errors/cancellation are handled before accessing results.
- Windows path comparisons respect directory boundaries and casing.

### Update checks removed

Automatic checks, the tray action, settings checkbox/property, update window and custom JSON parser were removed at the user's request. An old saved configuration key may remain but is unused. Release API requests are gone.

### Search and directory discovery

- Query terms are prepared once. Matching is ordinal and case-insensitive, appropriate for filename search and independent of UI culture.
- File patterns are prepared once and matched during a single enumeration; overlapping filters and overlapping configured roots produce no duplicate rows.
- Directory traversal is iterative, supports cancellation between entries, and does not descend into directory reparse points (links are still shown).
- Directory scanning and filename matching are independent of WPF models.
- Translation resources reuse one ResourceManager.

### Watchers

- Concurrent bursts schedule at most one pending dispatcher notification and a 150 ms debounce.
- Full directory snapshots are prepared in the background. Changed events are handled as well as create/delete/rename events.
- A snapshot is discarded if a newer change arrived while it loaded.
- Watcher errors invalidate icon caches and trigger a full rescan. Failed watchers are disposed and recreated in background, including paths unavailable at startup. Each path retries independently after 5 seconds with exponential backoff capped at 60 seconds; successful reconnection triggers a full rescan. Obsolete watcher errors are ignored and late connections are disposed during shutdown.
- Existing rows and selection are reused where possible. Icon transfers are versioned so delayed callbacks cannot overwrite a newer result.

### Network server discovery

- Bare network servers use `NetShareEnum` level 1 instead of launching a shell and parsing localized `net view` output.
- Enumeration handles multiple pages, deduplicates names, preserves spaces/Unicode, and lists visible disk shares only. Native buffers are released even on errors.
- Access denial reaches the existing no-access menu state. Cancellation is checked between pages and entries; a native network call already in progress cannot be interrupted.
- Regression tests cover pagination, share types, duplicate names, cancellation, access denial and non-progressing responses. Real unavailable-share timing and server permissions still require end-to-end validation.

### Icons and lists

- Two background STA workers and a 256-entry producer queue replace sixteen workers and an unbounded queue.
- Directory discovery applies backpressure and checks cancellation while waiting for queue space; it does not drop icons to enforce the limit.
- Bitmap conversion stays on STA workers, and final view-model updates are dispatched to the UI.
- Both caches are FIFO-bounded independently by the existing cache-limit setting. Invalidation removes bookkeeping as well as values; no forced GC is used.
- Pending factories are shared within an invalidation generation. Older factories cannot repopulate invalidated caches.
- List virtualization uses recycling and pixel scrolling. Row geometry uses the actual container or a filtered-index fallback.
- Text width is measured once per dataset to prevent menu width changes during recycling.

## Validation and measurement

`Measurements/search-benchmark.json` compares the old and new search predicates with 2,000 synthetic entries and 100 iterations, on the local Release runtime. This is a microbenchmark, not a complete application benchmark; culture semantics differ intentionally for filename matching.

Regression checks include real named pipes, Windows filter equivalence, directory cancellation, concurrent cache access, change coalescing, STA icon loading while the UI dispatcher is blocked, and an invisible WPF menu with 2,000 entries, scrolling and filtering. They also cover stale icon transfer and avoiding duplicate opacity application.

For measurements on your actual folders, launch from PowerShell:

```powershell
$env:HITNGONE_DIAGNOSTICS = '1'
& .\bin\x64\Release\net10.0-windows10.0.22000.0\win-x64\HitNGone.exe
```

The existing log then records discovery, presentation, watcher refresh and search/layout durations with private memory, working set, thread and handle counts. Diagnostics are off by default. Timings exclude completion of asynchronous WPF rendering/animations. Native filesystem or shell calls already in progress cannot always be interrupted immediately.

### Hotkeys

- German legacy modifiers `Strg` and `Umschalt` are accepted case-insensitively alongside invariant names. Saved combinations use the existing invariant serializer.
- Empty, incomplete and unknown combinations do not register unintended keys. Invalid nonempty input preserves an active registration; an empty combination explicitly unregisters it.
- A startup registration conflict leaves the configured hotkey saved, allowing it to work again after the conflicting application closes and HitNGone restarts. The tray menu remains available during a conflict.
- Regression tests verify parsing and serialization under German, English and Turkish cultures, actual Windows registration, duplicate-registration failure, preservation, release and reuse. Test registrations are always released and no keystrokes are injected.

## Remaining work

1. End-to-end testing and cold/warm measurements on actual local and unavailable network folders, including multiple monitors/DPI and visual positioning.
2. Validate watcher reconnection and native share discovery against real network servers (VPN loss, NAS restart, permissions). Automated tests cover missing local paths becoming available, actual subsequent file events, failure/recovery, obsolete errors and shutdown during connection.
3. Further separate shell actions/file metadata from RowData and configuration dialogs from settings storage, guided by actual extension needs.
4. Run the updated GitHub/Azure pipelines in the user's account; review the remaining StyleCop dependency separately. Both pipelines now use the VS 2026 hosted image, the pinned SDK and Build.ps1 -Publish. The identical build/test/publish command is validated locally; hosted execution has not been performed.

Windows file-dialog integration remains a separate later feature.

## HitNGone rename

- Project, solution, executable, namespaces, resources, translations and test project use HitNGone. Upstream license, authorship and links are preserved; original README is archived as UPSTREAM.md.
- Project files and Git history moved to C:\GitHub\Repos\HitNGone. The former QuickMenu directory is empty but may remain locked by the current Codex session; open the new directory for subsequent work.
- Existing user settings are copied from SystemTrayMenu if no HitNGone configuration exists; source files are retained. Portable settings accept the original section name. Relative root/icon paths are resolved during migration when the old installation can be found in the standard Program Files location. Other custom installation locations may require selecting the root again. Root paths are resolved against the application directory instead of the launcher's working directory.
- HitNGone crash dialogs use local logs and no longer offer to send fork-specific crash reports to upstream authors.
- Build.ps1 -Publish runs regression tests and creates artifacts\HitNGone\HitNGone.exe with its required files. Store packaging remains a separate legacy target with publisher/signing setup still required.
Validation after relocation: Release build and all 23 regression groups pass without compiler/analyzer warnings; portable publishing succeeds. The expanded settings regression also covers resolving a relative legacy root. Runtime startup with the imported local folder and the existing IPC wakeup handshake were verified. The window automation did not expose the tray menu, so visual positioning, interaction and multiple-monitor/DPI checks remain open. The original installed SystemTrayMenu process was left running; only agent-launched HitNGone test instances were stopped.
Latest hotkey validation: Release build without compiler/analyzer warnings; all 25 regression groups pass. Portable output is refreshed via Build.ps1 -Publish.
