# 🧹 CleanSweep

A cross-platform, CleanMyMac-style disk & memory cleaner built with **.NET 10 + Avalonia UI**,
wrapped in a translucent **liquid-glass** interface. One codebase, native on
**Windows and macOS** (and Linux).

## Features

A left **sidebar** switches between sections; results show on the right.

### 🔍 Smart Scan
Finds reclaimable space across categories, each with per-item sizes and checkboxes:

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

### 👯 Duplicate Files
Scans your dev/user roots and groups **byte-for-byte identical** files using three
escalating passes (exact size → cheap 64 KB partial hash → full SHA-256, only on
collisions). Keep one copy and select the rest for deletion. Bounded by depth and a
skip-list of huge/system directories. Opt-in, confirmation-gated.

### 📦 App Uninstaller
Lists installed applications (Windows: HKLM/HKCU uninstall registry keys; macOS:
`.app` bundles under `/Applications` and `~/Applications`) with name, publisher, version
and size. Uninstalling launches the app's own uninstaller on Windows, or moves the
bundle and its `~/Library` support files to the Trash on macOS. High-risk — always confirmed.

### 🚀 Startup Items Manager
Lists what launches at login and lets you **enable/disable** (it prefers a reversible
disable over deletion). Windows: Run keys (toggled via the same StartupApproved state
Task Manager uses), the user/common Startup folders (rename = disable), and Task
Scheduler logon tasks. macOS: `~/Library/LaunchAgents` and System Events login items.

### 🧠 Free Up RAM
Trims process working sets (Windows `EmptyWorkingSet`) or runs `purge` (macOS), with a
live before/after memory gauge.

## 🤖 AI "What is this?" explainer

Not sure whether something is safe to delete? Click the **?** on any scan item for a
short AI explanation — *what it is, whether it's safe to delete, why, and a
recommendation* — shown with a colored risk badge (Safe / Caution / Risky).

- **Bring any AI key.** Choose a provider in **Settings**: Anthropic (Claude, via the
  official C# SDK), OpenAI, Google Gemini, or any **OpenAI-compatible** endpoint
  (Groq, OpenRouter, DeepSeek, a local server…) by entering its base URL.
- **Set it in-app** (Settings → paste key, pick model), or via environment variables
  (`ANTHROPIC_API_KEY` / `OPENAI_API_KEY`, optional `CLEANSWEEP_AI_MODEL`). In-app
  settings are stored locally and take effect immediately.
- Uses **structured output** so responses are compact and parse reliably; results are
  cached in-memory.
- **Degrades gracefully:** with no key (or on any error) it falls back to a built-in
  offline heuristic for common items — it never crashes or blocks the UI.

The app ships a translucent **liquid-glass** theme with a light/dark/system toggle.

## Run it

```bash
# from the repo root
dotnet run --project src/CleanSweep
```

The correct OS implementation (paths, memory, app/startup management) is selected
automatically at runtime.

## Build a standalone app

Self-contained, single-file builds — no .NET install required to run.

```powershell
# Windows  ->  publish/win-x64/CleanSweep.exe
./scripts/publish-windows.ps1
```

```bash
# macOS (Apple Silicon)  ->  publish/CleanSweep.app
./scripts/publish-macos.sh
```

Or invoke the SDK directly:

```bash
dotnet publish src/CleanSweep -c Release -r win-x64  --self-contained -p:PublishSingleFile=true
dotnet publish src/CleanSweep -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

## Test

```bash
dotnet test
```

The engine has an xUnit suite (`tests/CleanSweep.Core.Tests`) covering the
safety-critical paths — `SafeDeleter` protection rules, duplicate grouping, the cleanup
modules, and AI graceful-degradation — all against synthesized temp trees with a fake
platform-paths double, so it's deterministic, OS-independent, and never touches real
system paths or the network.

## Architecture

```
CleanSweep.sln(x)
├─ src/CleanSweep.Core          ← platform-agnostic engine (no UI / no SDK deps)
│   ├─ Platform/                ← IPlatformPaths + Windows/Mac implementations
│   ├─ Cleaning/                ← ICleanupModule + modules + ScanEngine
│   ├─ Memory/                  ← IMemoryManager + Windows/Mac implementations
│   ├─ Duplicates/              ← IDuplicateFinder + size/partial/full-hash finder
│   ├─ Apps/                    ← IAppInventory + Windows/Mac implementations
│   ├─ Startup/                 ← IStartupManager + Windows/Mac implementations
│   ├─ AI/                      ← IItemExplainer + models + offline heuristic
│   ├─ Services/                ← FileSystemScanner, SafeDeleter, CommandLine
│   └─ Models/
├─ src/CleanSweep.AI            ← Anthropic SDK isolated here (AnthropicItemExplainer)
├─ src/CleanSweep               ← Avalonia app (MVVM, CommunityToolkit.Mvvm)
│   ├─ ViewModels/
│   ├─ Views/                   ← liquid-glass sidebar shell
│   └─ Services/                ← dialog service
├─ tests/CleanSweep.Core.Tests  ← xUnit engine tests
└─ scripts/                     ← publish-windows.ps1 / publish-macos.sh
```

Adding a new clean target is usually a few lines: implement `ICleanupModule`
(or subclass `DirectoryCleanupModule`) and register it in `ScanEngine`. Each new
capability sits behind a small Core interface (`IDuplicateFinder`, `IAppInventory`,
`IStartupManager`, `IItemExplainer`) with per-OS implementations selected at runtime.

## Safety

- **`SafeDeleter`** refuses to delete any path that *is* — or *contains* — a protected
  location (OS dirs, drive roots, your home folder, Documents/Desktop/Pictures, iCloud,
  keychains). Every destructive action routes through it.
- Risky/irreversible categories (**Developer Junk**, **Large Files**, **Duplicates**,
  **uninstall**, **login-item removal**) are **never pre-selected** and are
  **confirmation-gated**.
- Startup items prefer a reversible **disable** over deletion.
- Every filesystem access is guarded; locked/permission-denied items are skipped, never fatal.

## Roadmap ideas

- Per-item "reveal in Finder/Explorer"
- Scheduled / background scans
- Selectable "keep" copy per duplicate group
- Signed & notarized macOS builds
