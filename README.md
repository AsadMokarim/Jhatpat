<p align="center">
  <img src="src/Filee.App/Assets/Icons/jhatpat.svg" alt="Jhatpat logo" width="128" height="128">
</p>

<h1 align="center">Jhatpat</h1>

<p align="center">
  <strong>A cross-platform desktop utility for everyday file and image workflows.</strong><br>
  Built on the open-source <a href="https://github.com/KnifeLemon/Filee">Filee</a> project, with an evolving focus on contextual radial tools, image cropping, background framing, compression, batch processing, and clipboard workflows.
</p>

<p align="center">
  <strong>Built by <a href="https://github.com/AsadMokarim">AsadMokarim</a></strong>
</p>

<p align="center">
  <a href="#features"><strong>Features</strong></a> ·
  <a href="#technology-stack"><strong>Tech Stack</strong></a> ·
  <a href="#project-background--development"><strong>Development Approach</strong></a> ·
  <a href="#upstream-acknowledgement"><strong>Upstream Attribution</strong></a> ·
  <a href="#platform-support-status"><strong>Platform Support</strong></a> ·
  <a href="#packaging--installation"><strong>Install & Packaging</strong></a> ·
  <a href="#development"><strong>Development</strong></a> ·
  <a href="#license"><strong>License</strong></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-in%20development-D97706" alt="Status: in development">
  <img src="https://img.shields.io/badge/Ubuntu%2024.04-Linux%20target-1D9E75" alt="Ubuntu 24.04 Linux target">
  <img src="https://img.shields.io/badge/Windows%2010%20%7C%2011-preview-534AB7" alt="Windows 10 and Windows 11 preview">
  <img src="https://img.shields.io/badge/macOS-preview-534AB7" alt="macOS preview">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-6A62C9" alt="MIT license"></a>
</p>

> [!NOTE]
> **Jhatpat is an experimental, actively developed desktop application.** It builds upon the open-source Filee project and is being developed with AI-assisted coding tools. Features and platform compatibility are still being reviewed and validated. See the platform support section for the current status.

## Overview

Jhatpat is an ongoing project exploring how everyday file and image workflows can be brought together in a desktop application. It builds on Filee's existing conversion infrastructure while exploring additional image-manipulation tools and workflow improvements.

The project is not yet a stable, fully validated release. Feature availability and behaviour may vary by platform, and the documentation will evolve as testing continues.

## Features

Jhatpat combines the file-conversion foundation inherited from [Filee](https://github.com/KnifeLemon/Filee) with an expanding set of image, PDF, and video tools. Feature availability can vary by platform and optional engine.

### Cursor-first workflows

- **Donut menu at your cursor:** Hold **Ctrl**, drag files, and drop them onto a format or tool in the donut menu that opens at your cursor.
- **Contextual actions:** Surface relevant formats and workflows for the files you select.
- **Clipboard workflows:** Paste supported images into the app and copy processed results.
- **Recent activity:** Reopen recent outputs, open them with the system's default application, and manage history.
- **Batch workflows:** Process multiple images with shared settings instead of repeating the same action file by file.

### Image tools

- **Crop images:** Use aspect-ratio presets such as 1:1, 4:3, 16:9 and 3:2, or crop freely with an interactive preview.
- **Frame images:** Add a background around an image using solid colours, gradients, custom image backgrounds or transparency. Adjust padding, corner radius, shadows and canvas dimensions.
- **Resize and compress:** Set custom dimensions, preserve aspect ratio, choose scale presets, and adjust quality for supported lossy formats.
- **Batch image compression:** Queue multiple images and apply common output, dimension and quality settings in one workflow.
- **Remove backgrounds:** Use the background-removal workflow where its required engine or integration is available. Results and dependencies may vary by setup.
- **Annotate images:** Add annotations to supported images using the image-annotation workflow.
- **Photo editing:** Adjust window/light effects, colour and detail controls to refine the look of a photo.
- **Create photo collages:** Arrange multiple photos into a single collage.
- **Presets:** Use the evolving collection of image and workflow presets, including framing, crop, compression and photo-adjustment options.

### PDF tools

- **Merge PDFs:** Combine multiple PDF files into one document.
- **Reorder pages:** Arrange pages in the desired order before exporting a merged PDF.
- **Split PDFs:** Separate documents into smaller outputs where supported.
- **PDF conversion and compression:** Use the inherited Filee conversion infrastructure and available engines for supported PDF workflows.

### Video tools

- **Trim video:** Select the portion of a clip to keep.
- **Crop video:** Adjust the visible frame for the desired composition.
- **Change speed:** Speed up or slow down supported clips.
- **Join videos:** Combine multiple clips into one output where supported.
- **Export frames:** Extract frames from a video as image files.
- **Split video:** Divide a video into separate segments.
- **Convert media:** Filee's existing media conversion infrastructure provides additional video and audio format workflows when the required engine is available.

### File conversion and format presets

- **Broad format coverage:** Retains Filee's conversion catalogue and preset/engine infrastructure for images, documents, spreadsheets, presentations, PDFs, e-books, audio, video, subtitles, archives, fonts, CAD, 3D models, structured data and geospatial files.
- **Per-file-type presets:** Configure relevant options such as image quality and dimensions, PDF pages and compression, video/audio settings, output folders and file naming.
- **Optional engines:** Some conversions require tools such as LibreOffice, Pandoc, calibre, Ghostscript or FFmpeg. These are not necessarily installed by default; see [the upstream engine documentation](https://github.com/KnifeLemon/Filee/blob/main/docs/ENGINES.md) and the repository's own packaging instructions.
- **Automatic multi-step conversions:** The inherited conversion engine can find supported routes that require more than one conversion step.

### Linux and Wayland

- **Wayland-aware workflows:** Jhatpat has received improvements intended to make it work better in Wayland sessions, including screen-capture integrations for supported desktop environments.
- **Desktop-specific behaviour:** Global drag gestures, screen capture and window/input behaviour can differ between Wayland compositors and X11. Available integrations depend on the desktop environment and installed utilities; not every workflow is guaranteed to behave identically everywhere.

> Jhatpat is actively developed. The feature list describes implemented or integrated workflows, but each feature and platform combination still needs continued testing. Optional engines, desktop-session limitations and release packaging may affect availability.

## Formats

Jhatpat builds on Filee's existing conversion catalogue. The categories below summarise the inherited format coverage; exact input/output pairs depend on the selected conversion, installed optional engines and the current build.

| Category | Representative formats and workflows |
|---|---|
| **Images** | JPG/JPEG, PNG, WEBP, AVIF, TIFF, BMP, GIF, ICO, HEIC and other raster formats; resizing, compression, crop and framing workflows |
| **Vector graphics** | SVG, SVGZ, EMF, WMF and other supported vector formats; conversion to PDF or raster images where supported |
| **PDF** | PDF to images and supported document formats; merge, page reordering, splitting and compression workflows |
| **Documents** | DOCX, ODT, RTF, TXT, HTML and other formats supported by the installed conversion engines |
| **Spreadsheets** | XLSX, XLS, ODS, CSV and TSV, plus other supported tabular formats |
| **Presentations** | PPTX, PPT, ODP and other supported presentation formats |
| **E-books** | EPUB, MOBI, AZW3, FB2, CBZ and other formats supported by the relevant engine |
| **Video** | MP4, MOV, MKV, WEBM, AVI and other formats supported by the available media engine; trim, crop, speed, join, frame export and split tools |
| **Audio** | MP3, M4A, AAC, WAV, FLAC, OGG, OPUS and other supported audio formats |
| **Subtitles** | SRT, ASS/SSA, WebVTT and other supported subtitle formats |
| **Archives** | ZIP, 7Z, TAR, TAR.GZ and other supported archive formats; extract or package files where supported |
| **Fonts** | TTF, OTF, WOFF and WOFF2, where supported |
| **CAD and 3D** | DWG, DXF, GLB, glTF, STL, OBJ and other formats supported by the inherited converters |
| **Structured and geospatial data** | JSON, YAML, TOML, XML, vCard, iCalendar, GeoJSON, KML, GPX and other supported data formats |

**Important:** This is a category-level overview, not a guarantee that every listed format converts to every other format. Some workflows require optional engines, and support can differ between platforms. Check the current release and engine documentation for exact compatibility.

## Screenshots

<p align="center">
  <img src="docs/images/jhatpat-donut-toolbar.png" alt="Jhatpat donut toolbar" width="600">
  <img src="docs/images/jhatpat-home.png" alt="Jhatpat home screen" width="800">
  

## Project Background & Development

Jhatpat began as an exploration of desktop productivity tools after I came across the “Reverse Engineer Anything” trend and discovered Tangerine, a desktop utility for macOS. While researching alternatives, I found Filee, an open-source project that became the foundation for this experiment.

I use AI-assisted coding tools to guide implementation, explore design ideas, and iterate on the application. My contribution so far has focused on product direction, feature planning, and guiding the development process through instructions to AI tools.

This is an ongoing learning and development project. I have not yet personally reviewed the codebase line by line, so generated code, application behaviour, security, and platform compatibility require continued review and validation. The project should not be interpreted as a claim that every implementation detail has been independently engineered or audited.

Jhatpat is an independent project derived from Filee. It is not affiliated with or endorsed by the creators of Tangerine or Filee.

## Technology Stack

The codebase currently uses or references the following technologies. Exact versions and platform-specific dependencies should be checked against the project files before contributing.

- **Runtime and language:** .NET 10 / C#
- **Desktop UI:** Avalonia UI, using XAML and a Fluent theme
- **Architecture:** Model–View–ViewModel (MVVM), with `CommunityToolkit.Mvvm`
- **2D graphics and text rendering:** SkiaSharp and HarfBuzzSharp
- **Image processing:** Magick.NET / ImageMagick
- **Input and global hotkeys:** SharpHook
- **Linux packaging:** `dpkg-deb`, `tar`, `gzip`, and Freedesktop desktop utilities

## Upstream Acknowledgement

Jhatpat builds upon **[Filee](https://github.com/KnifeLemon/Filee)**.

- **Upstream repository:** https://github.com/KnifeLemon/Filee
- **Original author:** Myeonggu Kim ([KnifeLemon](https://github.com/KnifeLemon)) and Filee contributors
- **Upstream license:** MIT, as identified by the upstream project; consult the upstream repository and included license notices for the applicable terms.

Jhatpat reuses and adapts parts of Filee's conversion catalog, preset schemas, engine infrastructure, and configuration workflows while exploring an expanded direction focused on image manipulation, framing, compression, and desktop productivity.

This acknowledgement does not replace any copyright, license, or third-party notices required by the upstream project or bundled dependencies. Please see `LICENSE` and `THIRD-PARTY-NOTICES.md`.

## Platform Support Status

Platform support is still being validated. The table below distinguishes the current development target from features that need testing on native systems.

| Platform | Current status | Notes |
| :--- | :--- | :--- |
| **Linux — Ubuntu 24.04 LTS x64** | Primary development target | Wayland compatibility has been improved, including supported desktop-specific screen-capture paths. Validate drag gestures, capture and input behaviour on the target compositor; Wayland and X11 integrations can differ. |
| **Windows 10 / 11 x64** | Preview; native testing required | A successful cross-platform build or publish does not confirm runtime behaviour. Explorer context-menu integration, notification icons, registry changes, and other Windows-specific features need testing on Windows. See [`docs/windows-compatibility-audit.md`](docs/windows-compatibility-audit.md), if present. |
| **macOS 14+ — ARM64 / x64** | Preview; validation required | Some platform infrastructure may be inherited from upstream Filee. Jhatpat-specific behaviour must be verified on supported macOS hardware before claiming compatibility. |

> [!WARNING]
> Do not interpret a successful build or package creation as proof that every feature works. Native runtime tests are required for each supported operating system and desktop environment.

## Packaging & Installation

Linux packaging scripts and release artifacts may change while the project is under development. The following commands reflect the current repository workflow and should be checked against the latest scripts and available artifacts.

### Ubuntu 24.04 LTS / Debian package

If `scripts/package-ubuntu.sh` is available in your checkout:

```bash
./scripts/package-ubuntu.sh --version 1.0.0
```

If the script completes successfully and creates the expected package, install it with:

```bash
sudo apt install ./build/dist/jhatpat_1.0.0_amd64.deb
```

### Standalone tarball

If a matching tarball and installer are present in `build/dist/`:

```bash
tar -xzf build/dist/jhatpat-1.0.0-linux-x64.tar.gz
cd jhatpat-1.0.0
./install.sh
```

For detailed package layout and removal instructions, see the [Ubuntu Packaging Guide](docs/ubuntu-packaging.md), if present.

**Note:** Version numbers and filenames above are examples. Use the actual version and artifact names produced by your build.

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Additional platform-specific tools may be needed for screen capture, packaging, or native integrations.

### Clone the repository

```bash
git clone https://github.com/InnoverseAMU/Jhatpat.git
cd Jhatpat
```

### Build

The solution file in this repository is named `Filee.slnx`:

```bash
dotnet build Filee.slnx
```

### Run the desktop application

```bash
dotnet run --project src/Filee.App
```

### Run tests

Run the relevant test projects and inspect the results rather than relying only on the process exit code or a successful build.

```bash
dotnet test tests/Filee.App.Tests
```

The test command above targets the application test project only. Run the other test projects separately if you need a broader test assessment. Some tests or native UI workflows may require platform-specific environments.

## Contributing & Feedback

Feedback, bug reports, and suggestions are welcome. Since Jhatpat is actively being developed, please include:

- Your operating system and version.
- Steps to reproduce the issue.
- Expected behaviour and what happened instead.
- Relevant logs or screenshots, with personal information removed.

Before contributing, review the upstream license obligations and third-party notices. Pull requests should describe the changes and the tests or manual checks performed.

## License

Jhatpat is distributed under the terms stated in the repository's [`LICENSE`](LICENSE) file. The upstream Filee project and bundled third-party components retain their applicable copyright and license terms; see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

Do not rely on this README alone to determine licensing obligations. Verify that the repository's license files and notices accurately reflect the upstream project and all redistributed components.

<!-- sponsors: src/Filee.App/Assets/Sponsors/sponsors.json -->
