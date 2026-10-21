<p align="center">
  <img src="src/Filee.App/Assets/Icons/jhatpat.svg" alt="Jhatpat" width="128" height="128">
</p>

<h1 align="center">Jhatpat</h1>

<p align="center">
  <b>A modern, cross-platform desktop productivity and image-processing application.</b><br>
  Built by extending the open-source <a href="https://github.com/KnifeLemon/Filee">Filee</a> project with contextual radial tools, advanced cropping, background framing, compression, batch workflows, and clipboard integration.
</p>

<p align="center">
  <a href="#features"><b>Features</b></a> ·
  <a href="#technology-stack"><b>Tech Stack</b></a> ·
  <a href="#upstream-acknowledgement"><b>Upstream Attribution</b></a> ·
  <a href="#platform-support-status"><b>Platform Support</b></a> ·
  <a href="#packaging--installation"><b>Install & Packaging</b></a> ·
  <a href="#development"><b>Development</b></a> ·
  <a href="#license"><b>License</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Ubuntu%2024.04%20LTS-verified-1D9E75" alt="Ubuntu 24.04 verified">
  <img src="https://img.shields.io/badge/Windows-build%20ready%20(preview)-534AB7" alt="Windows build ready">
  <img src="https://img.shields.io/badge/macOS%2014+-preview-534AB7" alt="macOS preview">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-6A62C9" alt="MIT license"></a>
</p>

---

> [!NOTE]
> **Jhatpat is actively under development.** Features described below are implemented in the current codebase and verified through automated test suites and Linux runtime packaging.

---

## Features

### 1. Contextual Radial Workflows & Image Tools
- **Cursor-Centric Donut Menu**: Hold a modifier key or trigger via context menu to invoke a circular menu directly under the mouse cursor.
- **Dedicated Image Tools Submenu**: Provides immediate access to **Crop**, **Background Framing**, and **Compress & Resize** for supported raster images without overcrowding the format conversion slices.

### 2. Interactive Image Cropping
- **Aspect Ratio Presets**: 1:1 (Square), 4:3, 16:9, 3:2, and freeform crop canvas.
- **Live Preview & Controls**: Precision handle dragging, numeric size indicators, and reset capabilities.
- **Non-Destructive Export**: Never overwrites the source file silently. Direct native clipboard copying via `Ctrl+C` / toolbar button.

### 3. Background Framing
- **Background Modes**: Solid colors (with visual color picker and hex input), linear gradients, custom image backgrounds, and transparent canvas.
- **Styling**: Configurable padding, rounded corner radius, drop shadow with adjustable elevation and softness.
- **Dimensions**: Configurable aspect ratios, preset dimensions, and custom width/height with live preview.

### 4. Image Resizing & Compression
- **Pixel-Exact Resizing**: Custom width and height fields with aspect ratio lock by default.
- **Preset Scale Factors**: Quick scaling to 25%, 50%, 75%, and 100%.
- **Configurable Quality**: Quality slider and presets (High, Balanced, Low) for lossy codecs (JPEG, WebP). Automatic lossless preservation for PNG.

### 5. Multi-File Batch Processing
- **Queue Management**: Add multiple files or entire folders with drag-and-drop support.
- **Shared Parameters**: Apply scaling, max dimensions, target formats, and compression quality across all queued images in parallel.
- **Safe Collision Handling**: Automatic non-destructive renaming (`image (1).png`) or skip existing files.
- **Progress & Cancellation**: Per-file progress, total percentage indicator, and immediate batch cancellation via `Escape` or button.

### 6. System Clipboard & Screen Capture
- **Clipboard Input**: Paste images directly into Jhatpat using the Home screen button or `Ctrl+V` (`Cmd+V` on macOS). Auto-purges temporary cached images after 24 hours.
- **Native Clipboard Output**: Copy processed images from Crop, Framing, and Resize overlays directly into system memory as native bitmaps with toast feedback.
- **Screen Capture**: Interactive region screenshot capture (native Wayland GNOME Shell D-Bus, Spectacle, or X11 ImageMagick `import`), routing captures immediately into Jhatpat's radial toolchain.

### 7. Workflow Polish, Recent Activity & Preferences
- **Recent History Actions**: Open converted files directly in system viewer (`xdg-open` / `open` / Windows Shell association), delete individual entries, or clear all history.
- **Persistent Preferences**: Configure default image format (`same`, `png`, `jpg`, `webp`), default compression quality, and maintain-aspect-ratio lock under **Settings → General**.
- **Session Memory**: Last-used export and batch directories are remembered across launches.
- **Full 7-Language Localization**: English (`en`), Español (`es`), हिन्दी (`hi`), 日本語 (`ja`), 한국어 (`ko`), Polski (`pl`), and 简体中文 (`zh-CN`).

---

## Technology Stack

- **Runtime & Language**: [.NET 10.0](https://dotnet.microsoft.com/) / C# 13
- **Desktop UI Framework**: [Avalonia UI 12.1.3](https://avaloniaui.net/) (Cross-platform XAML GUI, Fluent Theme)
- **Architecture Pattern**: Model-View-ViewModel (MVVM) via `CommunityToolkit.Mvvm`
- **2D Graphics & Rendering**: [SkiaSharp 3.119](https://github.com/mono/SkiaSharp) and [HarfBuzzSharp](https://github.com/mono/SkiaSharp)
- **Image Processing Engines**: [Magick.NET-Q8-AnyCPU](https://github.com/dlemstra/Magick.NET) (ImageMagick)
- **Input & Hotkey Capture**: [SharpHook 8.0.1](https://github.com/TolikPylypchuk/SharpHook)
- **Packaging Tools**: Debian `dpkg-deb`, `tar`, `gzip`, Freedesktop `desktop-file-utils`

---

## Upstream Acknowledgement

Jhatpat is built upon the open-source **Filee** project:
- **Upstream Repository**: [https://github.com/KnifeLemon/Filee](https://github.com/KnifeLemon/Filee)
- **Original Author**: Myeonggu Kim ([KnifeLemon](https://github.com/KnifeLemon)) and Filee contributors.
- **License**: MIT License.

Jhatpat maintains backwards compatibility with Filee's offline conversion catalog (250+ formats), preset schemas, engine infrastructure, and configuration files, while introducing an expanded product direction focused on image manipulation, framing, compression, and enhanced desktop workflows.

---

## Platform Support Status

| Platform | Build Status | Runtime Verification Status | Notes |
| :--- | :--- | :--- | :--- |
| **Linux (Ubuntu 24.04 LTS x64)** | **Verified** | **Verified** (Wayland & X11) | Native `.deb` and standalone `.tar.gz` packages available. All features tested. |
| **Windows 10 / 11 (x64)** | **Verified** | **Preview / Pending Windows Device Testing** | Cross-publishes cleanly (`win-x64` self-contained .NET 10). Bundles `libSkiaSharp.dll` and `Magick.Native`. Full Explorer extension and registry integration require native Windows host testing (see [`docs/windows-compatibility-audit.md`](docs/windows-compatibility-audit.md)). |
| **macOS 14+ (ARM64 / x64)** | **Inherited** | **Preview** | Inherits upstream Filee macOS backend. Ad-hoc signed. |

> [!WARNING]
> While Windows x64 compilation and self-contained publishing succeed without errors, Windows-specific features (such as Explorer context menu shell extensions, taskbar notification icons, and registry keys) must be verified on a native Windows installation before claiming full Windows certification.

---

## Packaging & Installation

### Ubuntu 24.04 LTS / Debian (`.deb`)
Repeatable package builds are produced using [`scripts/package-ubuntu.sh`](scripts/package-ubuntu.sh):
```bash
./scripts/package-ubuntu.sh --version 1.0.0
```

Install via `apt`:
```bash
sudo apt install ./build/dist/jhatpat_1.0.0_amd64.deb
```

Or install standalone tarball without root:
```bash
tar -xzf build/dist/jhatpat-1.0.0-linux-x64.tar.gz
cd jhatpat-1.0.0
./install.sh
```

For detailed instructions, layout details, and uninstallation procedures, see the [Ubuntu Packaging Guide](docs/ubuntu-packaging.md).

---

## Development

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Building and Running
```bash
# Clone the repository
git clone https://github.com/AsadMokarim/Jhatpat.git Jhatpat
cd Jhatpat

# Build the solution
dotnet build Filee.slnx

# Run the desktop application
dotnet run --project src/Filee.App

# Run unit tests
dotnet test tests/Filee.App.Tests
```

---

<!-- sponsors: src/Filee.App/Assets/Sponsors/sponsors.json -->

## License

Jhatpat is licensed under the [MIT License](LICENSE).
Copyright (c) 2026 Jhatpat contributors, Myeonggu Kim (KnifeLemon), and Filee contributors.
Bundled third-party components retain their respective licenses as detailed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
