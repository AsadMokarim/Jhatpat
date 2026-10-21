# Jhatpat License & Third-Party Dependency Compliance Audit

**Date:** October 2026  
**Repository:** `~/Projects/Jhatpat/Filee`  
**Application:** Jhatpat (derivative desktop application extending upstream Filee)  
**Target Environments:** Linux (Ubuntu 24.04 LTS x64 / Debian-based), Windows x64, macOS 14+  
**Audit Type:** Pre-Distribution Open-Source Licensing & Compliance Verification  

> [!IMPORTANT]
> **Legal Notice & Disclaimer:** This document provides an engineering, technical, and open-source license compliance audit conducted from authoritative source repository files, package manifests (`Directory.Packages.props`, `project.assets.json`, `.nuspec`), and build outputs. It does not constitute formal legal advice, attorney-client privileged counsel, or legal certification.

---

## 1. Executive Summary

A comprehensive pre-distribution license audit was executed on the Jhatpat application codebase, published binaries, packaging scripts, and external engines. 

### Compliance Verdict: **CONDITIONAL — REMEDIATION REQUIRED BEFORE PUBLIC RELEASE**

1. **Upstream Permissive Heritage:** Upstream Filee is licensed under the permissive MIT License. Jhatpat is legally permitted to create a derivative work, rebrand the user-facing presentation, add proprietary or open features, and redistribute the application, provided the original MIT copyright and permission notices remain present in all copies.
2. **Critical Packaging Non-Compliance:** The current Ubuntu packaging pipeline (`scripts/package-ubuntu.sh`) produces `.deb` and `.tar.gz` distributions that **omit all license texts and copyright notices** (no `/usr/share/doc/jhatpat/copyright`, no `LICENSE`, and no `THIRD-PARTY-NOTICES.md` installed on disk). This directly violates the mandatory attribution conditions of the MIT, Apache-2.0, BSD, MPL, and LGPL licenses.
3. **Copyleft & Dynamic Linking Boundaries:** The application dynamically loads precompiled LGPL-3.0 and LGPL-2.1 shared libraries (`libuiohook.so` from SharpHook, and native delegate libraries statically linked inside `Magick.Native-Q8-x64.dll.so`). Because these libraries are loaded dynamically, the main application is not infected with GPL/LGPL copyleft; however, distribution requires shipping LGPL/GPL license texts and providing clear instructions or written offers for the corresponding source code.
4. **Isolated Process Engines:** Downstream conversion engines with viral copyleft licenses (Pandoc [GPL-2.0+], Calibre [GPL-3.0], FFmpeg [GPL-3.0+], Ghostscript [AGPL-3.0]) are **not bundled** into the application packages; they are downloaded on-demand and invoked exclusively across OS process boundaries via CLI arguments and stdout/stdin pipes. This establishes an arms-length separation that preserves Jhatpat's licensing autonomy.

---

## 2. Upstream Filee License & Derivative Obligations

### Upstream Identity & License
- **Upstream Project:** Filee (`https://github.com/KnifeLemon/Filee`)
- **Original Author & Copyright:** `Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors`
- **License Type:** MIT License (verbatim in root `LICENSE` file)

### Permitted Actions Under MIT
The MIT license grants broad permissions without payment or royalty:
- Free use, reproduction, modification, branching, merging, publishing, sublicensing, and distribution in source or binary forms.
- Rebranding the user-facing product identity from "Filee" to "Jhatpat" is permitted and represents standard practice to prevent trademark and origin confusion.

### Mandatory Compliance Conditions for Jhatpat
1. **Notice Retention:** The verbatim MIT license text and the copyright notice `Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors` **must be included in all copies or substantial portions of the Software** (both source code distributions and binary packaging).
2. **Derivative Copyright Assertion:** Jhatpat contributors may assert copyright over their modifications and additions alongside the upstream notice:
   ```text
   Copyright (c) 2026 Jhatpat contributors
   Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors
   ```
3. **Attribution Integrity:** Upstream attribution in `README.md`, `THIRD-PARTY-NOTICES.md`, and the in-app `AboutPage` must be preserved. Jhatpat has correctly retained upstream credits in user-facing documentation.
4. **Warranty & Liability Disclaimer:** The standard capital-letter disclaimer of warranty and limitation of liability must remain intact.

---

## 3. Comprehensive Dependency License Inventory

The solution relies on 73 resolved NuGet packages, native shared objects, UI geometries, and external engines. Authoritative license metadata was extracted directly from the NuGet cache manifests (`*.nuspec`) and package contents.

### A. Direct & Transitive NuGet Packages

| Package Name | Version | Author / Maintainer | Authoritative License (SPDX) | Redistribution Conditions / Obligations |
| :--- | :--- | :--- | :--- | :--- |
| **Avalonia** (and all core modules) | 12.1.3 | AvaloniaUI OÜ | **MIT** | Retain copyright notice & permission disclaimer. |
| **Avalonia.Desktop** | 12.1.3 | AvaloniaUI OÜ | **MIT** | Retain copyright notice & permission disclaimer. |
| **Avalonia.Themes.Fluent** | 12.1.3 | AvaloniaUI OÜ | **MIT** | Retain copyright notice & permission disclaimer. |
| **Avalonia.Angle.Windows.Natives** | 2.1.27548 | Google / ANGLE Authors | **BSD-3-Clause** | Retain copyright, condition list, disclaimer; no endorsement. |
| **AvaloniaUI.DiagnosticsSupport** | 2.2.3 | AvaloniaUI OÜ | *None in nuspec* | Excluded from Release builds (`Configuration != Debug`). |
| **CommunityToolkit.Mvvm** | 8.4.2 | Microsoft Corporation | **MIT** | Retain copyright notice & permission disclaimer. |
| **Microsoft.Extensions.\*** | 10.0.12 | Microsoft Corporation | **MIT** | Retain copyright notice & permission disclaimer. |
| **Microsoft.Maui.Graphics** | 10.0.110 | Microsoft Corporation | **MIT** | Retain copyright notice & permission disclaimer. |
| **Microsoft.IO.RecyclableMemoryStream** | 3.0.1 | Microsoft Corporation | **MIT** | Retain copyright notice & permission disclaimer. |
| **System.Security.Cryptography.Pkcs**| 10.0.13 | Microsoft Corporation | **MIT** | Retain copyright notice & permission disclaimer. |
| **Magick.NET-Q8-AnyCPU** | 14.17.2 | Dirk Lemstra | **Apache-2.0** | Retain license, notice, attribution; state changes. |
| **Magick.NET.Core** | 14.17.2 | Dirk Lemstra | **Apache-2.0** | Retain license, notice, attribution. |
| **SkiaSharp** (and native assets) | 3.119.4 | Microsoft / Mono / Google | **MIT** (wrapper) / **BSD-3-Clause** (Skia) | Retain copyright notice & BSD conditions. |
| **HarfBuzzSharp** (and native assets)| 8.3.1.3 | Microsoft / Mono / HarfBuzz | **MIT** (wrapper) / **Old MIT** (HarfBuzz) | Retain copyright notice & license text. |
| **PDFsharp** | 6.2.4 | empira Software GmbH | **MIT** | Retain copyright notice & permission disclaimer. |
| **PDFtoImage** | 5.2.1 | Frank Sungaila | **MIT** | Retain copyright notice & permission disclaimer. |
| **bblanchon.PDFium.\*** | 147.0.7690 | Benoit Blanchon / Google | **Apache-2.0** / **BSD-3-Clause** | Retain copyright and Apache/BSD license texts. |
| **PdfPig** | 0.1.16 | UglyToad / BobLd | **Apache-2.0** | Retain Apache-2.0 license & attribution notice. |
| **Unhwp** | 0.13.0 | iyulab | **MIT** | Retain copyright notice & permission disclaimer. |
| **Markdig** | 1.4.0 | Alexandre Mutel | **BSD-2-Clause** | Retain copyright notice & 2-clause disclaimer. |
| **Tomlyn** | 2.10.1 | Alexandre Mutel | **BSD-2-Clause** | Retain copyright notice & 2-clause disclaimer. |
| **YamlDotNet** | 18.1.0 | Antoine Aubry | **MIT** | Retain copyright notice & permission disclaimer. |
| **ExcelNumberFormat** | 1.1.0 | Anders Nygaard | **MIT** | Retain copyright notice & permission disclaimer. |
| **ExcelDataReader** | 3.9.0 | ExcelDataReader Project | **MIT** | Retain copyright notice & permission disclaimer. |
| **MimeKitLite** | 4.18.1 | Jeffrey Stedfast | **MIT** | Retain copyright notice & permission disclaimer. |
| **MsgReader** | 6.2.0 | Kees van Spelde | **MIT** | Retain copyright notice & permission disclaimer. |
| **OpenMcdf** | 3.3.0 | Federico Blaseotto | **MPL-2.0** | File-level copyleft. Source of modified files must be released. Unmodified DLL distribution requires MPL-2.0 notice & source pointer. |
| **UTF.Unknown** | 2.7.0 | CharsetDetector Project | **MPL-1.1** | File-level copyleft. Source availability notice required. |
| **RtfPipe** | 2.0.7677 | Eric Domke | **MIT** *(unspecified in nuspec)* | Package nuspec lacks `<license>` tag; GitHub repo is MIT. |
| **ACadSharp** | 3.8.0 | Albert Domenech | **MIT** | Retain copyright notice & permission disclaimer. |
| **SharpGLTF.Core** | 1.0.7 | Vicente Penades | **MIT** | Retain copyright notice & permission disclaimer. |
| **SharpCompress** | 1.0.0 | Adam Hathcock | **MIT** | Retain copyright notice & permission disclaimer. |
| **SharpHook** | 8.0.1 | Anatoliy Pylypchuk | **MIT** (managed) / **LGPL-3.0** (native) | SharpHook C# layer is MIT, but bundles native `libuiohook` (LGPL-3.0). See Native Libraries below. |
| **Svg.Skia** | 5.1.1 | Wiesław Šoltés | **MIT** | Retain copyright notice & permission disclaimer. |
| **Svg.Custom** (transitive of Svg.Skia)| 5.1.1 | Wiesław Šoltés / Svg.NET | **MS-PL** (Microsoft Public License) | Retain copyright, patent, trademark notices; terms comply with MS-PL. |
| **ExCSS** (transitive of Svg.Skia) | 4.3.1 | Tyler Brinks / Wiesław Šoltés | **MIT** | Retain copyright notice & permission disclaimer. |
| **Tmds.DBus.Protocol** | 0.94.1 | Tom Deseyn | **MIT** | Retain copyright notice & permission disclaimer. |

---

### B. Bundled Native Shared Libraries (Self-Contained Linux x64)

The published Linux application bundles the following compiled `.so` binaries in `/usr/lib/jhatpat/`:

1. **`libuiohook.so` (and `libuiohook-x11.so`, `libuiohook-wayland.so`, `libuiohook-xrecord.so`)**:
   - **Source:** Fork of `libuiohook` by Tolik Pylypchuk (`https://github.com/TolikPylypchuk/libuiohook`).
   - **License:** **GNU Lesser General Public License v3.0 (LGPL-3.0)**.
   - **Compliance Analysis:**
     - The native library is dynamically loaded at runtime by `SharpHook.dll` using `NativeLibrary.Load`.
     - *Relinking / Replacement Condition:* Met, because the library is a standalone shared object file that can be replaced by the end user with an ABI-compatible custom compilation.
     - *Notice & Source Obligation:* Jhatpat **must** include the text of the LGPL-3.0 and GPL-3.0 licenses in its distribution, clearly notify users that `libuiohook` is licensed under LGPL-3.0, and provide a direct hyperlink or written offer for the corresponding source code (`https://github.com/TolikPylypchuk/libuiohook`).

2. **`Magick.Native-Q8-x64.dll.so` (37 MB)**:
   - **Source:** Magick.NET native layer by Dirk Lemstra (`https://github.com/dlemstra/Magick.NET`).
   - **License:** **Apache-2.0**, but statically compiles **38 distinct third-party C/C++ delegate libraries**.
   - **Embedded Delegates & Licenses:**
     - *LGPL-2.1+ / LGPL-3.0:* `libheif` (1.23.5), `libde265` (1.1.3), `cairo` (1.18.6), `pango` (1.45.3), `glib` (2.64.3), `gdk-pixbuf` (2.44.8), `libcroco` (0.6.13), `liblqr` (0.4.2), `librsvg` (2.40.20).
     - *BSD / MIT / zlib / FTL / Apache:* `libjpeg-turbo`, `libpng`, `libtiff`, `libwebp`, `freetype`, `harfbuzz`, `openjpeg`, `openjph`, `openexr`, `imath`, `brotli`, `zstd`, `zlib`, `fontconfig`, `fribidi`, `libxml2`, `libzip`, `libhwy`, `aom`.
     - *BSD-2-Clause with Cisco Patent Grant:* `openh264` (2.6.0).
     - *CDDL-1.0 / LGPL-2.1:* `libraw` (0.22.2).
   - **Compliance Analysis:**
     - Magick.NET provides an authoritative 8,091-line `Notice.txt` inside its NuGet package documenting every delegate license and copyright.
     - Because `Magick.Native-Q8-x64.dll.so` is loaded dynamically by `Magick.NET-Q8-AnyCPU.dll`, the LGPL delegates do not force copyleft onto Jhatpat.
     - *Obligation:* The delegate attributions and full license texts from `Notice.txt` must be accessible to users in the distribution documentation.

3. **`libpdfium.so` (5.8 MB)**:
   - **Source:** PDFium binaries (`bblanchon.PDFium.Linux`).
   - **License:** **BSD-3-Clause / Apache-2.0**.
   - **Obligation:** Include Google PDFium copyright notices and BSD/Apache license terms.

4. **`libSkiaSharp.so` (11 MB) & `libHarfBuzzSharp.so` (2.7 MB)**:
   - **Source:** Google Skia & HarfBuzz.
   - **License:** **BSD-3-Clause** (Skia) & **Old MIT / ISC** (HarfBuzz).
   - **Obligation:** Include Skia and HarfBuzz copyright notices and disclaimers.

5. **`libunhwp.so` (5.0 MB)**:
   - **Source:** iyulab/unhwp native Rust library.
   - **License:** **MIT**.
   - **Obligation:** Include Unhwp copyright notice.

6. **CoreCLR Native Runtime Components (`libcoreclr.so`, `libclrjit.so`, etc.)**:
   - **Source:** .NET 10 Runtime (Microsoft Corporation).
   - **License:** **MIT**.
   - **Obligation:** Retain Microsoft .NET copyright notice.

---

### C. External Engines & Architectural Isolation

Jhatpat integrates with external engines for complex format conversions:

| Engine | License | Bundled or Downloaded | Isolation Boundary | Copyleft Risk to Jhatpat |
| :--- | :--- | :--- | :--- | :--- |
| **rhwp** | MIT | Downloaded on demand (Linux/macOS) / Bundled in Win Setup | CLI executable | **None** (Permissive) |
| **7-Zip** (`7z.dll`, `7z.exe`) | GNU LGPL-2.1+ / unRAR restriction | Downloaded on demand (Linux) / Bundled in Win Setup | CLI / Standalone DLL | **Low** (LGPL allows distribution; unRAR restriction forbids creating RAR archives, which Jhatpat does not do) |
| **Pandoc** | GPL-2.0-or-later | Downloaded on demand via `engines.json` | Subprocess via `Process.Start` | **None** (Separate process isolation) |
| **Calibre** (`ebook-convert`)| GPL-3.0 | Downloaded on demand via `engines.json` | Subprocess via `Process.Start` | **None** (Separate process isolation) |
| **FFmpeg** | GPL-3.0-or-later | Downloaded on demand via `engines.json` | Subprocess via `Process.Start` | **None** (Separate process isolation) |
| **Ghostscript** | AGPL-3.0 | Downloaded on demand via `engines.json` | Subprocess via `Process.Start` | **None** (Separate process isolation) |
| **LibreOffice** | MPL-2.0 | Downloaded on demand via `engines.json` | Subprocess via `soffice --headless` | **None** (Separate process isolation) |
| **H2Orestart** | GPL-3.0 | Downloaded on demand (LibreOffice extension) | Subprocess inside LibreOffice JVM | **None** (Runs in external engine) |
| **Eclipse Temurin JRE** | GPL-2.0 w/ Classpath Exception | Downloaded on demand (for H2Orestart) | External runtime environment | **None** (Classpath Exception) |

> [!NOTE]
> **Subprocess Isolation Principle:** Under established open-source legal interpretation (including the Free Software Foundation's GPL FAQ on "mere aggregation" and separate program invocation), executing an external program via standard command-line pipes, file arguments, and exit codes does not create a derived work. Because Jhatpat does not link against GPL/AGPL libraries and only invokes these tools as standalone child processes, Jhatpat remains an independent work and is not subject to reciprocal GPL/AGPL requirements.

---

### D. Fonts, Icons, and Visual Assets

1. **Material Design Icons (`src/Filee.App/Styles/Icons.axaml`)**:
   - Geometries extracted from Pictogrammers Material Design Icons.
   - **License:** **Apache-2.0**.
   - **Condition:** Retain Apache-2.0 notice and credit Pictogrammers.
2. **Simple Icons (`src/Filee.App/Styles/Brands.axaml`)**:
   - Geometries for GitHub Sponsors, Patreon, Ko-fi, PayPal logos.
   - **License:** **CC0-1.0 (Public Domain Dedication)**.
   - **Condition:** Brand trademarks remain property of their respective owners; used strictly for external hyperlinking.
3. **Application Branding Icons (`filee.png`, `filee.svg`, `filee.ico`)**:
   - Custom graphical artwork created for the project.
   - **License:** Covered under the main application MIT license.
4. **Noto Sans Fonts (`ThemeService.cs`, `Program.cs`)**:
   - Font family references (`fonts:Filee#Noto Sans`, `Noto Sans JP`, `Noto Sans KR`, `Noto Sans SC`, `Noto Sans Devanagari`).
   - Physical font files are **not bundled** in application binaries or packages; the application resolves fonts from the host operating system.
   - **License:** SIL Open Font License 1.1 (OFL-1.1).

---

## 4. Packaging Compliance Audit (Ubuntu `.deb` & `.tar.gz`)

An inspection of the generated Debian package (`build/dist/jhatpat_1.0.0_amd64.deb`) and standalone tarball (`build/dist/jhatpat-1.0.0-linux-x64.tar.gz`) revealed a critical compliance gap:

### Findings from `dpkg-deb -c jhatpat_1.0.0_amd64.deb`

```text
Installed Files:
./usr/bin/jhatpat -> /usr/lib/jhatpat/Filee
./usr/bin/jhatpat-cli -> /usr/lib/jhatpat/filee-cli
./usr/lib/jhatpat/* (all DLLs, .so files, and runtime configs)
./usr/share/applications/com.jhatpat.app.desktop
./usr/share/icons/hicolor/*
```

### Deficiencies Identified:
1. **Missing `/usr/share/doc/jhatpat/copyright`:**
   - Debian Policy §12.5 specifies: *"Every package must be accompanied by a verbatim copy of its copyright notice and its distribution license in the file `/usr/share/doc/<package>/copyright`."*
   - The `.deb` package completely omits `/usr/share/doc/jhatpat/`.
2. **Missing `LICENSE` & `THIRD-PARTY-NOTICES.md` in Distributions:**
   - Neither the root `LICENSE` nor `THIRD-PARTY-NOTICES.md` is copied to `/usr/lib/jhatpat/` or `/usr/share/doc/jhatpat/` in the Debian package.
   - The portable `.tar.gz` archive extracts application binaries, scripts, and desktop files, but contains no license file in the root archive directory.
3. **Redistribution Non-Compliance:**
   - Distributing compiled binaries without their accompanying copyright notice and permission notice violates the express terms of the MIT, Apache-2.0, BSD-2/3-Clause, and LGPL licenses.

---

## 5. Risk & Anomaly Flags

| Flag # | Component | Severity | Description & Compliance Risk | Required Action / Fix |
| :---: | :--- | :---: | :--- | :--- |
| **FLAG-1** | **`RtfPipe` (v2.0.7677)** | **Medium** | Package nuspec omits the `<license>` tag entirely. While author Eric Domke licenses the project under MIT on GitHub, the distributed NuGet artifact lacks authoritative license metadata. | Document explicit MIT attribution with GitHub source link in `THIRD-PARTY-NOTICES.md`. |
| **FLAG-2** | **`Svg.Custom` (v5.1.1)** | **Medium** | Transitive dependency of `Svg.Skia`. Licensed under **MS-PL** (Microsoft Public License), but upstream's `THIRD-PARTY-NOTICES.md` classified all Svg components as MIT. | Update `THIRD-PARTY-NOTICES.md` to distinguish `Svg.Custom` as MS-PL and include MS-PL terms. |
| **FLAG-3** | **`libuiohook` inside `SharpHook`** | **Major** | `SharpHook` nuspec declares MIT, but bundles native binary `libuiohook.so` which is **LGPL-3.0**. Distributing the `.deb` package without LGPL-3.0 notices or source code instructions is non-compliant. | Include LGPL-3.0 license text in `/usr/share/doc/jhatpat/copyright` and provide source repository URL (`TolikPylypchuk/libuiohook`). |
| **FLAG-4** | **`Magick.Native` Delegate Notices** | **Major** | Statically bundles 38 delegate libraries into `Magick.Native-Q8-x64.dll.so`, including LGPL-2.1+/3.0 libraries (`libheif`, `libde265`, `cairo`, `pango`, `glib`). The 8,000-line `Notice.txt` from Magick.NET is omitted from packaging. | Install `Notice.txt` or an authoritative summary into `/usr/share/doc/jhatpat/` and link it in `THIRD-PARTY-NOTICES.md`. |
| **FLAG-5** | **HEVC / H.265 Patent Pool (`libde265` / `libheif`)** | **Medium** | While `libde265` is open-source (LGPL-3.0), the H.265/HEVC video/image compression standard is encumbered by patents managed by MPEG LA and HEVC Advance. | Note patent risk for commercial distributions. Non-commercial and standard open-source distributions generally rely on individual end-user exemptions. |
| **FLAG-6** | **7-Zip unRAR Code Restriction** | **Low** | The RAR unpacking code in `7z.dll` is licensed under the unRAR restriction (cannot be used to re-create the RAR compression algorithm or write RAR files). | Jhatpat only reads/extracts RAR files, fully complying with this restriction. Disclose this condition clearly in notices. |

---

## 6. Verified Technical Facts vs. Legal Review Items

To maintain legal precision and avoid overstepping engineering audit boundaries, findings are divided into verified facts and items requiring formal legal advice:

### Verified Technical & Factual Findings
- Upstream Filee source files carry the standard MIT License notice.
- Published Linux self-contained packages bundle 23 native shared objects (`.so`), including dynamic loaders for `libuiohook` and `Magick.Native`.
- In-app GUI (`AboutPage.axaml`) loads and renders `avares://Filee/Assets/THIRD-PARTY-NOTICES.md` at runtime via `MarkdownView`.
- Generated packages in `build/dist/` (`.deb` and `.tar.gz`) do not contain any license files or Debian copyright files on disk.
- External conversion tools (Pandoc, Ghostscript, FFmpeg, Calibre) are executed as separate child processes via `System.Diagnostics.Process` and are not linked into the Jhatpat assemblies.

### Items Requiring Legal / Corporate Review
- **Commercial Patent Liability:** If Jhatpat is distributed commercially or sold, legal counsel should evaluate HEVC patent licensing for `libde265`/`libheif`.
- **Debian Archive Distribution:** If Jhatpat seeks inclusion in official Debian/Ubuntu repositories (Debian *main*), dependencies with non-free restrictions (such as 7-Zip unRAR) must be segregated to *non-free*, or replaced with free alternatives (e.g., `unar` or `libarchive`).
- **Trademark Policy:** Formal trademark clearance for the name "Jhatpat" in desktop utility software across target jurisdictions (US, EU, etc.).

---

## 7. Recommended Remediation & Repository Structure

To bring Jhatpat into full compliance before distribution, the following modifications should be implemented:

### A. Packaging Automation Enhancement (`scripts/package-ubuntu.sh`)
Update the packaging script to generate a standard Debian machine-readable copyright file at `/usr/share/doc/jhatpat/copyright` and copy documentation files into both distributions:

1. **For Debian Package (`.deb`):**
   - Create `/usr/share/doc/jhatpat/`
   - Install `/usr/share/doc/jhatpat/copyright` (DEP-5 compliant)
   - Install `/usr/share/doc/jhatpat/THIRD-PARTY-NOTICES.md.gz`
   - Install `/usr/share/doc/jhatpat/README.md.gz`
2. **For Portable Tarball (`.tar.gz`):**
   - Copy `LICENSE` and `THIRD-PARTY-NOTICES.md` to the archive root.
   - Update `install.sh` to copy these notices to `$INSTALL_DIR/doc/` or `$INSTALL_DIR/`.

---

### B. Compliant Debian `copyright` File Template (DEP-5 Format)

The following machine-readable copyright file should be placed at `/usr/share/doc/jhatpat/copyright`:

```text
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Jhatpat
Upstream-Contact: Jhatpat Developers <info@jhatpat.app>
Source: https://github.com/KnifeLemon/Filee

Files: *
Copyright: 2026 Jhatpat contributors
           2026 Myeonggu Kim (KnifeLemon) and Filee contributors
License: MIT

Files: src/Filee.App/Styles/Icons.axaml
Copyright: Pictogrammers / Material Design Icons contributors
License: Apache-2.0

Files: src/Filee.App/Styles/Brands.axaml
Copyright: Simple Icons contributors
License: CC0-1.0

Files: usr/lib/jhatpat/libuiohook*.so
Copyright: 2021-2026 Anatoliy Pylypchuk
           2006-2020 Alexander Barker
License: LGPL-3.0-or-later

Files: usr/lib/jhatpat/Magick.Native-Q8-x64.dll.so
Copyright: 1999-2026 ImageMagick Studio LLC
           Dirk Lemstra
License: Apache-2.0 and LGPL-2.1-or-later and BSD-3-Clause and zlib

License: MIT
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in all
 copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 SOFTWARE.

License: Apache-2.0
 On Debian systems, the complete text of the Apache License Version 2.0
 can be found in `/usr/share/common-licenses/Apache-2.0'.

License: LGPL-3.0-or-later
 On Debian systems, the complete text of the GNU Lesser General Public License
 Version 3 can be found in `/usr/share/common-licenses/LGPL-3'.
 Source code for libuiohook is available at: https://github.com/TolikPylypchuk/libuiohook
```

---

### C. Updates to `THIRD-PARTY-NOTICES.md`

Update the repository root `THIRD-PARTY-NOTICES.md` to:
1. Explicitly list `Svg.Custom` under `MS-PL` with a link to `https://licenses.nuget.org/MS-PL`.
2. Add reference to `Notice.txt` from Magick.NET for the 38 embedded delegate libraries.
3. Ensure the LGPL-3.0 dynamic linking and source availability statement for `libuiohook` is prominent.

---

## 8. Conclusion

From an architectural and technical standpoint, Jhatpat is built on a compliant foundation. The upstream MIT license permits derivative extension and rebranding, and process isolation shields the core application from copyleft viral contamination by external conversion tools. 

To achieve distribution readiness, the packaging pipeline must be updated to install the required license and notice files on the target filesystem. Implementing the DEP-5 copyright file and bundling `THIRD-PARTY-NOTICES.md` in `.deb` and `.tar.gz` packages will fulfill all legal attribution obligations across the software supply chain.
