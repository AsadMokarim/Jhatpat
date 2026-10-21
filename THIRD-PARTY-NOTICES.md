# Third-party notices

Jhatpat is an MIT-licensed derivative application extending Filee. It uses and redistributes the following third-party software.
Each component remains under its own license.

## Libraries (NuGet)

| Component | License | Project |
|---|---|---|
| Avalonia UI | MIT | https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| SharpHook | MIT | https://github.com/TolikPylypchuk/SharpHook |
| libuiohook (native hook inside SharpHook) | GNU LGPL-3.0-or-later | https://github.com/TolikPylypchuk/libuiohook |
| Magick.NET / ImageMagick | Apache-2.0 / ImageMagick License | https://github.com/dlemstra/Magick.NET |
| ImageMagick delegate libraries (libheif, libde265, libwebp, libjpeg-turbo, libpng, libtiff, zlib, …) | Various (LGPL-2.1+, LGPL-3.0, BSD, zlib, MIT) | https://github.com/dlemstra/Magick.NET |
| PDFsharp | MIT | https://github.com/empira/PDFsharp |
| PDFtoImage | MIT | https://github.com/sungaila/PDFtoImage |
| PDFium (bblanchon.PDFium) | BSD-3-Clause / Apache-2.0 | https://pdfium.googlesource.com/pdfium |
| SkiaSharp | MIT (wrapper) / BSD-3-Clause (Skia) | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp | MIT (wrapper) / Old MIT (HarfBuzz) | https://github.com/mono/SkiaSharp |
| Svg.Skia (incl. Svg.Model, Svg.Animation, Svg.SceneGraph, ExCSS) | MIT | https://github.com/wieslawsoltes/Svg.Skia |
| Svg.Custom (custom SVG rendering core) | MS-PL (Microsoft Public License) | https://github.com/wieslawsoltes/Svg.Skia |
| SharpCompress (zstd decompression for engine downloads; RAR, 7z and TAR comics) | MIT | https://github.com/adamhathcock/sharpcompress |
| Unhwp | MIT | https://github.com/iyulab/unhwp |
| Markdig | BSD-2-Clause | https://github.com/xoofx/markdig |
| ExcelNumberFormat | MIT | https://github.com/andersnm/ExcelNumberFormat |
| ExcelDataReader | MIT | https://github.com/ExcelDataReader/ExcelDataReader |
| PdfPig (PDF text, layout and pictures) | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| MimeKitLite (e-mail / MIME parsing) | MIT | https://github.com/jstedfast/MimeKit |
| MsgReader (Outlook .msg reading) | MIT, Copyright (c) Kees van Spelde | https://github.com/Sicos1977/MSGReader |
| OpenMcdf (OLE compound files, used by MsgReader) | MPL-2.0 (source: the project page) | https://github.com/openmcdf/openmcdf |
| RtfPipe (RTF → HTML; nuspec lacks license tag, repo is MIT) | MIT, Copyright (c) 2018 Eric Domke | https://github.com/erdomke/RtfPipe |
| UTF.Unknown (charset detection, used by MsgReader) | MPL-1.1 (source: the project page) | https://github.com/CharsetDetector/UTF-unknown |
| Microsoft.Maui.Graphics, Microsoft.IO.RecyclableMemoryStream (used by MsgReader) | MIT | https://github.com/dotnet/maui |
| YamlDotNet (YAML reading and writing) | MIT, Copyright (c) Antoine Aubry and contributors | https://github.com/aaubry/YamlDotNet |
| Tomlyn (TOML reading) | BSD-2-Clause, Copyright (c) Alexandre Mutel | https://github.com/xoofx/Tomlyn |
| ACadSharp (DWG/DXF reading and writing; includes CSMath and CSUtilities) | MIT, Copyright (c) Albert Domenech | https://github.com/DomCR/ACadSharp |
| SharpGLTF.Core (glTF / GLB reading and writing) | MIT, Copyright (c) Vicente Penades | https://github.com/vpenades/SharpGLTF |
| AngleSharp | MIT | https://github.com/AngleSharp/AngleSharp |
| pypandoc-hwpx (Pandoc AST mapping and package layout in `Filee.Engines/Hwp/Hwpx`, incl. `blank.hwpx`) | MIT, Copyright (c) 2024 pypandoc-hwpx Contributors | https://github.com/msjang/pypandoc-hwpx |
| Microsoft.Extensions.* | MIT | https://github.com/dotnet/runtime |
| cu2qu (fontTools): cubic-to-quadratic approach followed by `Filee.Engines/Fonts/Cff/CubicToQuadratic.cs` | Apache-2.0, Copyright 2016 Google Inc. | https://github.com/fonttools/fonttools |
| Microsoft.ML.OnnxRuntime (neural foreground segmentation engine) | MIT, Copyright (c) Microsoft Corporation | https://github.com/microsoft/onnxruntime |

## Machine Learning Models

| Model | License | Upstream Project |
|---|---|---|
| U²-Net / U²-Netp (Qin et al., salient object and portrait segmentation) | Apache-2.0, Copyright (c) 2020 Xuebin Qin | https://github.com/xuebinqin/U-2-Net |

### libuiohook (LGPL-3.0-or-later)

`libuiohook` (the native input hook inside SharpHook) is the fork https://github.com/TolikPylypchuk/libuiohook (upstream https://github.com/kwhat/libuiohook), licensed under the GNU LGPL-3.0 (`COPYING.LESSER.md` in its repository). Jhatpat loads SharpHook's unmodified build of it dynamically at runtime (`libuiohook.so` on Linux, `uiohook.dll` on Windows, `libuiohook.dylib` on macOS). Under LGPL-3.0 Section 4, users are entitled to replace those shared libraries with their own compatible build of libuiohook.

### Svg.Custom (MS-PL)

`Svg.Custom` is licensed under the Microsoft Public License (MS-PL, https://licenses.nuget.org/MS-PL). This license grants copyright and patent permissions to use, reproduce, and distribute the code, provided that all original copyright, patent, trademark, and attribution notices are retained in redistributions.

### Magick.NET and Delegate Libraries

`Magick.Native-Q8-x64.dll.so` (and `Magick.Native-Q8-x64.dll` on Windows) statically incorporates 38 delegate libraries (e.g., `libheif`, `libde265`, `cairo`, `pango`, `glib`, `gdk-pixbuf`, `libcroco`, `liblqr`, `librsvg`, `libpng`, `libjpeg-turbo`, `libwebp`, `libtiff`, `zlib`, `openjpeg`, `openh264`, `openexr`, `freetype`, `harfbuzz`, `fontconfig`, `aom`, `libraw`, etc.). The complete, authoritative 8,000+ line notice file `Notice.txt` from Magick.NET is bundled at `/usr/share/doc/jhatpat/Magick.NET-Notice.txt` with Ubuntu packages.

### RtfPipe

`RtfPipe` (v2.0.7677.4303) lacks a `<license>` XML element in its published NuGet manifest (.nuspec). Upstream author Eric Domke licenses the project under the MIT License in the authoritative GitHub repository: https://github.com/erdomke/RtfPipe.

Used by the tests only (not redistributed): `DocumentFormat.OpenXml` (Open XML SDK, MIT, https://github.com/dotnet/Open-XML-SDK) validates the DOCX files Filee writes.

## Engines

Bundled with the installer (Windows installer only; external on Linux):

| Component | License | Project |
|---|---|---|
| rhwp | MIT | https://github.com/edwardkim/rhwp |
| 7-Zip (console: `7z.exe`, `7z.dll`) | GNU LGPL-2.1-or-later; parts of `7z.dll`: LGPL with the unRAR license restriction, BSD-3-Clause, BSD-2-Clause | https://www.7-zip.org |
| Microsoft Visual C++ Redistributable (Windows: next to `Filee.exe` and `rhwp.exe`, conda-forge `vc14_runtime`) | Microsoft Visual C++ Redistributable license | https://github.com/conda-forge/vc-feedstock |

The Linux packages do not bundle external engines. If the user invokes rhwp, it can be built from unmodified source (tag `v0.8.7`, commit `1a76570e833917d15817415a53c09ad61ab3203f`) or obtained as a standalone engine. It is under the MIT license; its `LICENSE` is located in `engines/rhwp`.

7-Zip is Copyright (C) 1999-2026 Igor Pavlov; its `License.txt` is installed next to it (`engines/7zip`). The RAR decompression code in `7z.dll` is under the unRAR license restriction: it may be used to unpack RAR archives, but not to develop a RAR (WinRAR) compatible archiver or to re-create the RAR compression algorithm. Jhatpat only reads RAR files. Source code: https://github.com/ip7z/7zip.

Downloaded from their official release pages when the user chooses to install them (pinned in `src/Filee.Engines/Infrastructure/engines.json`):

| Component | License | Project |
|---|---|---|
| LibreOffice | MPL-2.0 | https://www.libreoffice.org |
| H2Orestart (HWP/HWPX import for LibreOffice) | GPL-3.0 | https://github.com/ebandal/H2Orestart |
| Eclipse Temurin JRE (for H2Orestart) | GPL-2.0 with Classpath Exception | https://adoptium.net |
| Pandoc | GPL-2.0-or-later | https://github.com/jgm/pandoc |
| Ghostscript (conda-forge build) | AGPL-3.0 | https://www.ghostscript.com |
| Microsoft Visual C++ Redistributable (for Ghostscript, conda-forge `vc14_runtime`) | Microsoft Visual C++ Redistributable license | https://github.com/conda-forge/vc-feedstock |
| FFmpeg 9.0.2 (gyan.dev "full_build-shared" Windows build; ffmpeg.exe, ffprobe.exe and their libraries, incl. x264, x265, libvpx, LAME, Opus, Vorbis, Theora, OpenCORE AMR) | GPL-3.0-or-later (configured with `--enable-gpl --enable-version3`); FFmpeg itself LGPL-2.1-or-later | https://ffmpeg.org |
| calibre (ebook-convert) | GPL-3.0 | https://calibre-ebook.com |

These programs run as separate processes via `Process.Start`. Their source code is available from the linked projects.

## Windows 11 Explorer menu (FileeExplorerMenu.dll)

The DLL is Filee's own C code, compiled with the Zig toolchain (a build tool, not shipped) against the mingw-w64 Windows headers, without a C runtime. Pieces of these projects that can end up in the binary:

| Component | License | Project |
|---|---|---|
| Zig compiler_rt (compiler support routines) | MIT | https://github.com/ziglang/zig |
| mingw-w64 Windows headers and import library definitions | Public domain / ZPL-2.1 | https://www.mingw-w64.org |

## Installer

`Filee-<version>-win-Setup.exe` is built with Inno Setup (a build tool, not part of the app); the setup and uninstall programs inside it are Inno Setup's.

| Component | License | Project |
|---|---|---|
| Inno Setup (Copyright (C) 1997-2026 Jordan Russell, portions Copyright (C) 2000-2026 Martijn Laan) | Inno Setup License | https://jrsoftware.org/isinfo.php |

## Fonts and icons

| Component | License | Project |
|---|---|---|
| Noto Sans, Noto Sans JP, Noto Sans KR, Noto Sans SC, Noto Sans Devanagari | SIL Open Font License 1.1 | https://github.com/notofonts |
| Test fonts in `tests/Filee.Engines.Tests/Fonts` (subsets of Noto Sans and Noto Sans KR; not shipped) | SIL Open Font License 1.1 | https://github.com/notofonts |
| Material Design Icons (Pictogrammers) | Apache-2.0 | https://pictogrammers.com |
| Simple Icons 16.35.0 (GitHub Sponsors, Patreon, Ko-fi and PayPal logos in the Sponsor window) | CC0-1.0 | https://simpleicons.org |

The GitHub Sponsors, Patreon, Ko-fi and PayPal names and logos are trademarks of their owners and are used only to link to each service.
