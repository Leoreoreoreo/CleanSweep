# 🧹 CleanSweep

A cross-platform, CleanMyMac-style disk & memory cleaner built with **.NET 10 + Avalonia UI**.
One codebase, native on **Windows and macOS** (and Linux).

## Features

**Smart Scan** finds reclaimable space across categories, each with per-item sizes and checkboxes:

| Category | What it finds |
|---|---|
| 🧹 Temporary Files | System & app temp dirs |
| 📦 Application Caches | Rebuildable app cache data (`~/Library/Caches`, Windows caches) |
| 📄 Log Files | Diagnostic logs safe to clear |
| 🗑️ Trash / Recycle Bin | Windows Recycle Bin (Shell API) / macOS `~/.Trash` |
| 🌐 Browser Caches | Chrome / Edge / Brave cache (Windows) |
| 🛠️ Developer Junk | `node_modules`, `__pycache__`, `bin`/`obj`, `target`, `.gradle`… (opt-in) |
| 📚 Package Caches | npm / pip / NuGet / Yarn download caches |
| 📂 Large & Old Files | Files ≥ 100 MB under your dev roots & Downloads (opt-in) |

**Free Up RAM** — trims process working sets (Windows `EmptyWorkingSet`) or runs `purge` (macOS),
with a live before/after memory gauge.

## Run it

```bash
# from the repo root
dotnet run --project src/CleanSweep
```

The correct OS implementation (paths + memory) is selected automatically at runtime.

### Build a standalone app

```bash
# Windows
dotnet publish src/CleanSweep -c Release -r win-x64 --self-contained
# macOS (Apple Silicon)
dotnet publish src/CleanSweep -c Release -r osx-arm64 --self-contained
```

## Architecture

```
CleanSweep.sln(x)
├─ src/CleanSweep.Core          ← platform-agnostic engine (no UI deps)
│   ├─ Platform/                ← IPlatformPaths + Windows/Mac implementations
│   ├─ Cleaning/                ← ICleanupModule + modules + ScanEngine
│   ├─ Memory/                  ← IMemoryManager + Windows/Mac implementations
│   ├─ Services/                ← FileSystemScanner, SafeDeleter
│   └─ Models/
└─ src/CleanSweep               ← Avalonia app (MVVM, CommunityToolkit.Mvvm)
    ├─ ViewModels/
    └─ Views/
```

Adding a new clean target is usually a few lines: implement `ICleanupModule`
(or subclass `DirectoryCleanupModule`) and register it in `ScanEngine`.

## Safety

- **`SafeDeleter`** refuses to delete any path that *is* — or *contains* — a protected
  location (OS dirs, drive roots, your home folder, Documents/Desktop/Pictures, iCloud, keychains).
- Risky categories (**Developer Junk**, **Large Files**) are **never pre-selected** — you opt in.
- Every filesystem access is guarded; locked/permission-denied items are skipped, never fatal.

## Roadmap ideas

- Duplicate-file finder · App uninstaller · Startup-items manager
- Per-item "reveal in Finder/Explorer" · scheduled scans · unit tests for the engine
