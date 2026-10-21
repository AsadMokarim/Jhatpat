# Ubuntu 24.04 LTS (x64) Packaging & Distribution Guide

This document describes the packaging, installation, and deployment workflows for **Jhatpat** on Ubuntu 24.04 LTS (Noble Numbat) and compatible Debian-based Linux distributions.

---

## 1. Distribution Overview

Jhatpat is distributed for Linux `x64` in two formats:

| Format | File Pattern | Typical Target Audience |
| :--- | :--- | :--- |
| **Debian Package (`.deb`)** | `jhatpat_<version>_amd64.deb` | Standard desktop installations with system package manager integration (`apt` / `dpkg`). |
| **Standalone Tarball (`.tar.gz`)** | `jhatpat-<version>-linux-x64.tar.gz` | Portable distribution with interactive `install.sh` / `uninstall.sh` supporting both root (`/opt`) and user-level (`~/.local`) installation. |

Both formats bundle a fully self-contained .NET 10 runtime, native graphics dependencies (SkiaSharp, HarfBuzz), and local assets. No external .NET SDK or runtime installation is required on the target machine.

---

## 2. Prerequisites & Dependencies

### Runtime Dependencies
- **Core C Runtime**: `libc6 (>= 2.34)`
- **Font Rendering**: `libfontconfig1`
- **X11 / Desktop Libraries**: `libx11-6`, `libice6`, `libsm6`
- **Recommended (Screenshots)**: `imagemagick` (provides `/usr/bin/import` for interactive cross-desktop screen capture)

To install runtime dependencies on Ubuntu:
```bash
sudo apt update
sudo apt install libc6 libfontconfig1 libx11-6 libice6 libsm6 imagemagick
```

### Build & Packaging Dependencies
To build the packages from source, the host environment needs:
- .NET 10 SDK (`dotnet`)
- `dpkg-deb` (part of `dpkg`)
- `tar` and `gzip`
- `desktop-file-validate` (recommended, part of `desktop-file-utils`)

---

## 3. Repeatable Packaging Script

The repository includes a packaging automation script at [`scripts/package-ubuntu.sh`](file:///home/asad/Projects/Jhatpat/Filee/scripts/package-ubuntu.sh).

### Syntax & Options
```bash
./scripts/package-ubuntu.sh [options]

Options:
  --version <ver>      Set package version (default: 1.0.0)
  --output-dir <dir>   Package output directory (default: build/dist)
  --skip-publish       Skip dotnet publish and reuse existing build/publish/linux-x64
  -h, --help           Show help
```

### Running the Packaging Pipeline
From the repository root (`~/Projects/Jhatpat/Filee`):
```bash
chmod +x scripts/package-ubuntu.sh
./scripts/package-ubuntu.sh --version 1.0.0
```

The script performs the following steps:
1. **Compiles & Publishes**: Builds `Filee.App` and `Filee.Cli` in `Release` configuration for `linux-x64` (`--self-contained true`).
2. **Assembles Debian Package**: Prepares standard FHS hierarchy in `build/deb-staging`, calculates package size, generates `DEBIAN/control`, `DEBIAN/postinst`, and `DEBIAN/postrm`.
3. **Validates Desktop File**: Runs `desktop-file-validate` on `com.jhatpat.app.desktop`.
4. **Builds `.deb` Package**: Calls `dpkg-deb --build --root-owner-group` to generate `build/dist/jhatpat_<version>_amd64.deb`.
5. **Assembles Portable Tarball**: Packages application files, icons, desktop launcher, `install.sh`, and `uninstall.sh` into `build/dist/jhatpat-<version>-linux-x64.tar.gz`.
6. **Verifies Package Integrity**: Executes a dry-run test extraction using `dpkg-deb -x`, verifies executable launch and command help, and computes SHA-256 checksums.

---

## 4. Debian Package Installation (`.deb`)

### Installation
Install the generated `.deb` package using `apt` (recommended for automatic dependency resolution):
```bash
sudo apt install ./build/dist/jhatpat_1.0.0_amd64.deb
```
Or using `dpkg`:
```bash
sudo dpkg -i ./build/dist/jhatpat_1.0.0_amd64.deb
sudo apt install -f  # resolve any missing dependencies if needed
```

### Installed Layout
| Path | Description |
| :--- | :--- |
| `/usr/lib/jhatpat/` | Complete self-contained application binaries and libraries. |
| `/usr/bin/jhatpat` | Symlink to `/usr/lib/jhatpat/Filee` (desktop GUI executable). |
| `/usr/bin/jhatpat-cli` | Symlink to `/usr/lib/jhatpat/filee-cli` (command-line tool). |
| `/usr/bin/filee` | Compatibility symlink to `/usr/lib/jhatpat/Filee`. |
| `/usr/bin/filee-cli` | Compatibility symlink to `/usr/lib/jhatpat/filee-cli`. |
| `/usr/share/applications/com.jhatpat.app.desktop` | Freedesktop application launcher with actions and MIME associations. |
| `/usr/share/icons/hicolor/scalable/apps/jhatpat.svg` | Vector application icon. |
| `/usr/share/icons/hicolor/256x256/apps/jhatpat.png` | Raster application icon (256×256). |
| `/usr/share/doc/jhatpat/copyright` | Machine-readable DEP-5 license and copyright notice. |
| `/usr/share/doc/jhatpat/THIRD-PARTY-NOTICES.md` | Full inventory of third-party libraries and licenses. |
| `/usr/share/doc/jhatpat/README.md` | User guide and application documentation. |

### Uninstallation
To remove the application:
```bash
sudo apt remove jhatpat
```
To remove the application and purge configuration metadata:
```bash
sudo apt purge jhatpat
```
The maintainer `postrm` script automatically updates `desktop-database`, `mime-database`, and `gtk-update-icon-cache`.

---

## 5. Portable Tarball Installation (`.tar.gz`)

For environments where `sudo` or system packages cannot be installed, or for running directly without package manager intervention.

### Extraction
```bash
tar -xzf build/dist/jhatpat-1.0.0-linux-x64.tar.gz
cd jhatpat-1.0.0
```

### System-Wide Installation (Root)
```bash
sudo ./install.sh
```
- Installs to `/opt/jhatpat`
- Symlinks to `/usr/local/bin/jhatpat` and `/usr/local/bin/jhatpat-cli`
- Installs desktop entry to `/usr/share/applications`
- Installs icons to `/usr/share/icons/hicolor`

### Per-User Installation (Non-Root)
```bash
./install.sh
```
- Installs to `~/.local/share/jhatpat`
- Symlinks to `~/.local/bin/jhatpat` and `~/.local/bin/jhatpat-cli`
- Installs desktop entry to `~/.local/share/applications`
- Installs icons to `~/.local/share/icons/hicolor`

### Uninstallation
Run the uninstallation script corresponding to the installation mode:
```bash
# For system-wide:
sudo /opt/jhatpat/uninstall.sh

# For per-user:
~/.local/share/jhatpat/uninstall.sh
```

---

## 6. Desktop Integration & Context Menu Behavior

### Freedesktop Launcher Specification
The desktop entry `com.jhatpat.app.desktop` conforms to Desktop Entry Specification 1.5:
```ini
[Desktop Entry]
Version=1.5
Type=Application
Name=Jhatpat
GenericName=Image Processing & Productivity
Comment=Crop, frame, resize, compress, and convert images
Exec=jhatpat --convert %F
Icon=jhatpat
Terminal=false
Categories=Graphics;2DGraphics;RasterGraphics;
MimeType=image/png;image/jpeg;image/webp;image/bmp;image/tiff;image/gif;
Keywords=jhatpat;filee;image;crop;framing;resize;compression;convert;
StartupWMClass=com.jhatpat.app
Actions=Crop;Framing;Resize;Batch;

[Desktop Action Crop]
Name=Crop Image
Exec=jhatpat --convert %F

[Desktop Action Framing]
Name=Background Framing
Exec=jhatpat --convert %F

[Desktop Action Resize]
Name=Compress & Resize
Exec=jhatpat --convert %F

[Desktop Action Batch]
Name=Batch Processing
Exec=jhatpat --convert %F
```

### File Manager Context Menus
- On GNOME Files (Nautilus), Dolphin, Nemo, and Thunar, right-clicking a supported raster image (`.png`, `.jpg`, `.webp`, `.bmp`, `.tiff`, `.gif`) displays Jhatpat under **"Open With"**.
- Opening an image with `jhatpat --convert %F` directly launches Jhatpat's radial menu, offering:
  - **Crop**
  - **Background Framing**
  - **Compress & Resize**
  - **Format Conversion** (PNG, JPG, WebP, PDF, etc.)
- Multi-file selection launches the contextual radial interface or batch queue.

---

## 7. Package Verification & Test Checklist

The packaging pipeline verifies:
1. `dpkg-deb -I`: Control file headers, package metadata, dependencies, and size calculation.
2. `dpkg-deb -c`: Package structure and file permissions (0755 for executables, 0644 for assets/libraries).
3. `desktop-file-validate`: Strict conformance of `.desktop` file syntax.
4. `dpkg-deb -x`: Sandbox unpacking to temporary directory to verify that `filee-cli` and `Filee` binaries run cleanly.
5. SHA-256 checksum generation for release distribution validation.
