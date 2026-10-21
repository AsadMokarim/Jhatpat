# Jhatpat Release Package Validation & Installation Guide

This document details the release package validation, platform-specific installation procedures, dependency analysis, upgrade behavior, and known environment limitations for **Jhatpat 1.0.0**.

---

## 1. Release Artifacts Overview

The release artifacts are generated under `build/dist/` via `scripts/package-ubuntu.sh`:

| Artifact | Type | Architecture | Size | Checksum (SHA-256) |
| :--- | :--- | :--- | :--- | :--- |
| `jhatpat_1.0.0_amd64.deb` | Native Debian package | `amd64` (x86_64) | ~72.0 MB | `1ada151bf34028b61c181e4e169e2b6c15d15a3b1368600da3beba92e89b4847` |
| `jhatpat-1.0.0-linux-x64.tar.gz` | Portable distribution archive | `linux-x64` | ~88.7 MB | `2393ccb9656f0e7fdd7abc8a544cf33f59ebcd619bf604ceef95203e11c1c40f` |

### Package Content Optimization
- **Symbols & Dev Docs Removed**: Build targets in `Directory.Build.props`, `Filee.App.csproj`, and `Filee.Cli.csproj` strip managed `.pdb` symbols and `.xml` compiler doc files from Release publish packages, saving over **3.06 MB** of unneeded metadata.
- **Zero Local Paths**: Verified via recursive inspection that no development machine paths (`/home/...`) exist inside text metadata, `.desktop` files, control scripts, or helper tools.

---

## 2. Tested Environments vs. Audited Platforms

> [!IMPORTANT]
> **Active Verification Scope**:
> - **Linux x86_64 (Ubuntu 24.04 LTS / X11)** was **actively tested** in an isolated clean environment (`/tmp/jhatpat_release_validation`).
> - **Windows 10/11** and **macOS (Sonoma/Sequoia)** were **statically audited** via installer definitions (`installer/Jhatpat.iss`, `installer/macos/Info.plist`, PowerShell build scripts) but were not executed on physical Windows/macOS hardware in this session.

---

## 3. Clean-Environment Linux Validation Results

Validation was conducted in an isolated user root with empty `HOME`, custom `XDG_DATA_HOME`, `XDG_CONFIG_HOME`, and `JHATPAT_DATA_DIR` environments.

### 3.1. Installation Verification
- **Portable Tarball**: Extracted and executed `install.sh` in unprivileged user mode.
  - Successfully deployed application files into `~/.local/share/jhatpat/` with executable permissions on `Filee` and `filee-cli`.
  - Created executable symlinks in `~/.local/bin/`:
    - `jhatpat` $\rightarrow$ `~/.local/share/jhatpat/Filee` (GUI launcher)
    - `jhatpat-cli` $\rightarrow$ `~/.local/share/jhatpat/filee-cli` (CLI tool)
    - `filee` $\rightarrow$ `~/.local/share/jhatpat/filee-cli` (Backwards compatibility)
    - `filee-cli` $\rightarrow$ `~/.local/share/jhatpat/filee-cli` (Backwards compatibility)
  - Installed FreeDesktop icon hierarchy (`16x16` through `512x512` PNGs and scalable SVG) into `~/.local/share/icons/hicolor/`.
  - Installed `com.jhatpat.app.desktop` into `~/.local/share/applications/`.
- **Debian Package Structure**:
  - `dpkg-deb -I` verified package name `jhatpat`, version `1.0.0`, section `graphics`, and architecture `amd64`.
  - Verified system dependencies: `libc6 (>= 2.34)`, `libgcc-s1`, `libfontconfig1`, `libx11-6`, `libice6`, `libsm6`.
  - Post-installation triggers configure `update-desktop-database` and `gtk-update-icon-cache`.

### 3.2. Desktop Integration & Specification Compliance
- **Desktop Entry Validation**:
  - Ran `desktop-file-validate` on `com.jhatpat.app.desktop`.
  - Validation result: **0 warnings, 0 errors** (Fully conforms to Desktop Entry Specification 1.5).
  - Executable action paths correctly resolve to the installed launcher directory (`$BIN_DIR/jhatpat --radial`, `$BIN_DIR/jhatpat --drop-zone`, etc.).
- **Window Management Class**:
  - `StartupWMClass=com.jhatpat.app` ensures desktop dock/taskbar grouping associates running windows with the launcher icon.

### 3.3. Dependency Integrity (`ldd` Audit)
All bundled shared libraries and native executables were verified with `ldd`. All dynamic dependencies are resolved against standard Ubuntu 24.04 libraries:
- `Filee`: OK (glibc, libpthread, libdl, libm)
- `filee-cli`: OK (glibc, libpthread, libdl, libm)
- `libSkiaSharp.so`: OK (libfontconfig, libfreetype, libm, libc)
- `libpdfium.so`: OK (libpthread, libm, libc)
- `libHarfBuzzSharp.so`: OK (libm, libc)
- `libuiohook.so`: OK (libX11, libXinerama, libXtst, libxkbcommon)
- `Magick.Native-Q8-x64.dll.so`: OK (statically embeds 38 delegate libraries, dynamically links libc/libm/libgomp)

### 3.4. First Launch & File-Processing Workflows
- **CLI Workflows**:
  - `jhatpat-cli version`: Executed successfully.
  - `jhatpat-cli formats`: Returned comprehensive list of supported raster, vector, document, and media formats.
  - `filee` alias: Verified full backward compatibility.
  - Image conversion test:
    - `800x600 RGBA PNG` $\rightarrow$ `WebP`: Succeeded (exit 0, valid output).
    - `800x600 RGBA PNG` $\rightarrow$ `JPEG`: Succeeded (exit 0, valid output).
- **GUI Launch**:
  - `jhatpat --background`: Started cleanly in headless X11 session (`DISPLAY=:1`), initialized single-instance lock and `TriggerService`, and shut down gracefully upon SIGTERM without error.
  - Configuration directory and `settings.json` store initialized cleanly under `~/.config/jhatpat/`.

### 3.5. Settings Persistence & Upgrade Test
- **Persistence Verification**: Custom user preferences (`Language: hi`, `Theme: Dark`, custom tags) were written to `settings.json`.
- **In-Place Upgrade**: Re-ran installer (`install.sh`) over existing installation.
  - Updated application files cleanly.
  - Verified that `settings.json` was **100% preserved** and custom user preferences remained intact.

### 3.6. Clean Uninstall Behavior
- Executed `~/.local/share/jhatpat/uninstall.sh`.
- Result:
  - `~/.local/share/jhatpat` completely purged.
  - Symlinks `~/.local/bin/jhatpat`, `jhatpat-cli`, `filee`, `filee-cli` removed.
  - `com.jhatpat.app.desktop` removed from `~/.local/share/applications`.
  - Icon theme directories cleaned up.
  - User preferences in `~/.config/jhatpat` preserved in accordance with Debian and FreeDesktop uninstall standards.

---

## 4. Platform-Specific Installation Instructions

### 4.1. Ubuntu / Debian Linux

#### Option A: Native Debian Package (Recommended for System-Wide Install)
```bash
# Install Debian package
sudo dpkg -i jhatpat_1.0.0_amd64.deb

# If any dependencies are missing:
sudo apt-get install -f

# Launch GUI
jhatpat

# Run CLI
jhatpat-cli convert image.png --to webp
```

To remove:
```bash
sudo apt-get remove jhatpat
```

#### Option B: Standalone Portable Archive (No Root Required)
```bash
# Extract tarball
tar -xzf jhatpat-1.0.0-linux-x64.tar.gz
cd jhatpat-1.0.0

# Install for current user into ~/.local/share/jhatpat and ~/.local/bin
./install.sh

# Ensure ~/.local/bin is in your PATH:
export PATH="$HOME/.local/bin:$PATH"

# Run Jhatpat
jhatpat
```

To uninstall:
```bash
~/.local/share/jhatpat/uninstall.sh
```

---

### 4.2. Windows (x64) — Specification & Audit Notes

- **Installer Mechanism**: Built via Inno Setup 7 (`installer/Jhatpat.iss`).
  - Output binary: `Jhatpat-1.0.0-win-Setup.exe`.
  - Installs to: `C:\Program Files\Jhatpat`.
  - Windows 11 Sparse Package: Registers `Filee.ExplorerMenu` package for top-level Windows 11 context menu integration.
  - Command-Line Shim: Installs `{app}\cli\filee.cmd` and `{app}\cli\jhatpat.cmd` on system `PATH`.
  - Settings Location: `%APPDATA%\Filee` (kept intact across upgrades and uninstalls).
- **Portable Distribution**:
  - Generated via `build/build-portable.ps1`.
  - Standalone zip archive with no administrator requirement.

---

### 4.3. macOS (Apple Silicon & Intel) — Specification & Audit Notes

- **Bundle Structure**: `Jhatpat.app` configured via `installer/macos/Info.plist`.
  - Identifiers: `com.jhatpat.app`, `CFBundleName=Jhatpat`.
  - Services Menu: Finder contextual action `"Convert with Jhatpat"`.
  - Settings Location: `~/Library/Application Support/Filee`.

---

## 5. Known Limitations & Environment Details

1. **Wayland Global Mouse Gestures**:
   - On native Wayland compositors (GNOME Wayland, Sway), compositors prohibit unrestricted background mouse tracking for security reasons.
   - On Wayland, Shift+Drag gesture detection relies on compositor portals or active drop zones. The global shortcut radial menu (`Super+Alt+Space` or custom key chord) should be used instead.
2. **First-Launch Initialization**:
   - On first cold launch, Jhatpat initializes embedded fonts and caches engine statuses asynchronously (~2-3 seconds). Subsequent launches and background idle memory remain lightweight (~70 MB RSS in background, ~200 MB with image engines loaded).
3. **External Optional Converters**:
   - Built-in converters (ImageMagick, SkiaSharp, PdfPig, Pdfium, Unhwp) work out of the box with zero external dependencies.
   - Optional external formats (e.g. DOCX $\rightarrow$ PDF via LibreOffice, video transcode via FFmpeg) require respective binaries installed on `$PATH` or downloaded via the optional engines dialog.
