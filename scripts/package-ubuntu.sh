#!/usr/bin/env bash
# package-ubuntu.sh: Build self-contained Ubuntu 24.04 (x64) packaging for Jhatpat
# Produces:
#   1. Standalone portable archive: jhatpat-<version>-linux-x64.tar.gz (with install.sh/uninstall.sh)
#   2. Native Debian package:       jhatpat_<version>_amd64.deb (with desktop entry, icons & postinst)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

VERSION="1.0.0"
OUTPUT_DIR="$REPO_ROOT/build/dist"
PUBLISH_DIR="$REPO_ROOT/build/publish/linux-x64"
SKIP_PUBLISH=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            VERSION="$2"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --skip-publish)
            SKIP_PUBLISH=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --version <version>    Set package version (default: 1.0.0)"
            echo "  --output-dir <dir>     Output directory for packages (default: build/dist)"
            echo "  --skip-publish         Skip dotnet publish and reuse existing build/publish/linux-x64"
            echo "  -h, --help             Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

echo "============================================================"
echo "Jhatpat Packaging for Ubuntu 24.04 LTS (x64)"
echo "Version:    $VERSION"
echo "Output:     $OUTPUT_DIR"
echo "Publish:    $PUBLISH_DIR"
echo "============================================================"

# Verify required tools
for tool in dotnet dpkg-deb tar gzip; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "Error: required tool '$tool' is not installed or not in PATH." >&2
        exit 1
    fi
done

mkdir -p "$OUTPUT_DIR"
BUILD_DIR="$REPO_ROOT/build"
DEB_STAGING="$BUILD_DIR/deb-staging"
TAR_STAGING="$BUILD_DIR/tarball-staging/jhatpat-$VERSION"

# 1. Publish self-contained Linux x64 binaries
if [ "$SKIP_PUBLISH" = false ] || [ ! -f "$PUBLISH_DIR/Filee" ]; then
    echo "==> Publishing Filee.App (linux-x64 Release)..."
    mkdir -p "$PUBLISH_DIR"
    dotnet publish "$REPO_ROOT/src/Filee.App/Filee.App.csproj" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        /p:PublishSingleFile=false \
        -o "$PUBLISH_DIR"

    echo "==> Publishing Filee.Cli (linux-x64 Release)..."
    dotnet publish "$REPO_ROOT/src/Filee.Cli/Filee.Cli.csproj" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        /p:PublishSingleFile=false \
        -o "$PUBLISH_DIR"
else
    echo "==> Reusing existing published binaries in $PUBLISH_DIR"
fi

chmod +x "$PUBLISH_DIR/Filee" "$PUBLISH_DIR/filee-cli"

# Asset sources
ICON_SVG="$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat.svg"
ICON_PNG="$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat.png"

# 2. Build Debian package (.deb)
echo "==> Constructing Debian package structure..."
rm -rf "$DEB_STAGING"
mkdir -p "$DEB_STAGING/DEBIAN"
mkdir -p "$DEB_STAGING/usr/lib/jhatpat"
mkdir -p "$DEB_STAGING/usr/bin"
mkdir -p "$DEB_STAGING/usr/share/applications"
mkdir -p "$DEB_STAGING/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$DEB_STAGING/usr/share/icons/hicolor/256x256/apps"

# Copy published application files to /usr/lib/jhatpat
cp -a "$PUBLISH_DIR/." "$DEB_STAGING/usr/lib/jhatpat/"
# Clean up any development debug symbols or XML documentation files from package payload
rm -f "$DEB_STAGING/usr/lib/jhatpat/"*.pdb "$DEB_STAGING/usr/lib/jhatpat/"*.xml

# Install copyright and license documentation
mkdir -p "$DEB_STAGING/usr/share/doc/jhatpat"
cat << 'EOF' > "$DEB_STAGING/usr/share/doc/jhatpat/copyright"
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Jhatpat
Upstream-Contact: Jhatpat Developers <info@jhatpat.app>
Source: https://github.com/KnifeLemon/Filee
Comment: Jhatpat is a rebranded derivative desktop application extending the upstream Filee project.

Files: *
Copyright: 2026 Jhatpat contributors
           2026 Myeonggu Kim (KnifeLemon) and Filee contributors
License: MIT

Files: usr/share/icons/hicolor/*
Copyright: 2026 Jhatpat contributors
           2026 Myeonggu Kim (KnifeLemon)
License: MIT

Files: usr/share/doc/jhatpat/*
Copyright: 2026 Jhatpat contributors
           2026 Myeonggu Kim (KnifeLemon) and Filee contributors
License: MIT

Files: usr/lib/jhatpat/libuiohook*.so
Copyright: 2021-2026 Anatoliy Pylypchuk
           2006-2020 Alexander Barker
License: LGPL-3.0-or-later
Comment: Native dynamic library for global input hooks from SharpHook (https://github.com/TolikPylypchuk/libuiohook). Loaded dynamically via NativeLibrary.Load; user replacement of this shared object is supported under LGPL-3.0 §4.

Files: usr/lib/jhatpat/Magick.Native-Q8-x64.dll.so
Copyright: 1999-2026 ImageMagick Studio LLC
           2014-2026 Dirk Lemstra
           and embedded delegate library authors
License: Apache-2.0 and ImageMagick and LGPL-2.1-or-later and LGPL-3.0-or-later and BSD-3-Clause and zlib
Comment: Native shared library dynamically loaded by Magick.NET. Statically incorporates 38 delegate libraries (libheif, libde265, cairo, pango, glib, libpng, libjpeg-turbo, libwebp, libtiff, zlib, openjpeg, openh264, openexr, freetype, harfbuzz, fontconfig, aom, libraw, etc.). Complete third-party notices are installed in /usr/share/doc/jhatpat/Magick.NET-Notice.txt.

Files: usr/lib/jhatpat/libpdfium.so
Copyright: 2014-2026 The Chromium Authors and Google LLC
           2019-2026 Benoit Blanchon
License: BSD-3-Clause and Apache-2.0

Files: usr/lib/jhatpat/libSkiaSharp.so
Copyright: 2011-2026 Google LLC
           2015-2026 Microsoft Corporation
License: BSD-3-Clause and MIT

Files: usr/lib/jhatpat/libHarfBuzzSharp.so
Copyright: 2010-2026 Google LLC
           2018-2026 Behdad Esfahbod
           2015-2026 Microsoft Corporation
License: Old-MIT and MIT

Files: usr/lib/jhatpat/libunhwp.so usr/lib/jhatpat/Unhwp.Net.dll
Copyright: 2024-2026 iyulab
License: MIT

Files: usr/lib/jhatpat/OpenMcdf*.dll
Copyright: 2008-2026 Federico Blaseotto
License: MPL-2.0
Comment: Upstream source available at https://github.com/openmcdf/openmcdf

Files: usr/lib/jhatpat/UtfUnknown.dll
Copyright: CharsetDetector Project contributors
License: MPL-1.1
Comment: Upstream source available at https://github.com/CharsetDetector/UTF-unknown

Files: usr/lib/jhatpat/UglyToad.PdfPig*.dll
Copyright: 2018-2026 UglyToad and BobLd
License: Apache-2.0

Files: usr/lib/jhatpat/Svg.Custom.dll
Copyright: 2021-2026 Wiesław Šoltés
           Svg.NET contributors
License: MS-PL

Files: usr/lib/jhatpat/Markdig.dll usr/lib/jhatpat/Tomlyn.dll
Copyright: Alexandre Mutel
License: BSD-2-Clause

Files: usr/lib/jhatpat/libcoreclr*.so usr/lib/jhatpat/libclr*.so usr/lib/jhatpat/libhost*.so usr/lib/jhatpat/libmscordac*.so usr/lib/jhatpat/libmscordbi.so usr/lib/jhatpat/libSystem*.so usr/lib/jhatpat/System*.dll usr/lib/jhatpat/Microsoft*.dll
Copyright: .NET Foundation and Contributors
           Microsoft Corporation
License: MIT

Files: usr/lib/jhatpat/libonnxruntime*.so
Copyright: Microsoft Corporation
License: MIT

Files: usr/lib/jhatpat/models/*
Copyright: 2020 Xuebin Qin
License: Apache-2.0
Comment: U²-Netp neural foreground segmentation model for portrait background removal.

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

License: MS-PL
 Microsoft Public License (MS-PL)
 .
 This license governs use of the accompanying software. If you use the software,
 you accept this license. If you do not accept the license, do not use the software.
 .
 1. Definitions
 The terms "reproduce," "reproduction," "derivative works," and "distribution" have
 the same meaning here as under U.S. copyright law.
 A "contribution" is the original software, or any additions or changes to the software.
 A "contributor" is any person that distributes its contribution under this license.
 "Licensed patents" are a contributor's patent claims that read directly on its contribution.
 .
 2. Grant of Rights
 (A) Copyright Grant- Subject to the terms of this license, including the license conditions
 and limitations in section 3, each contributor grants you a non-exclusive, worldwide,
 royalty-free copyright license to reproduce its contribution, prepare derivative works of
 its contribution, and distribute its contribution or any derivative works that you create.
 (B) Patent Grant- Subject to the terms of this license, including the license conditions and
 limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free
 license under its licensed patents to make, have made, use, sell, offer for sale, import,
 and/or otherwise dispose of its contribution in the software or derivative works of the
 contribution in the software.
 .
 3. Conditions and Limitations
 (A) No Trademark License- This license does not grant you rights to use any contributors' name,
 logo, or trademarks.
 (B) If you bring a patent claim against any contributor over patents that you claim are infringed
 by the software, your patent license from such contributor to the software ends automatically.
 (C) If you distribute any portion of the software, you must retain all copyright, patent, trademark,
 and attribution notices that are present in the software.
 (D) If you distribute any portion of the software in source code form, you may do so only under this
 license by including a complete copy of this license with your distribution. If you distribute any
 portion of the software in compiled or object code form, you may only do so under a license that
 complies with this license.
 (E) The software is licensed "as-is." You bear the risk of using it. The contributors give no
 express warranties, guarantees or conditions. You may have additional consumer rights under your
 local laws which this license cannot change. To the extent permitted under your local laws, the
 contributors exclude the implied warranties of merchantability, fitness for a particular purpose
 and non-infringement.

License: Apache-2.0
 On Debian systems, the complete text of the Apache License Version 2.0
 can be found in `/usr/share/common-licenses/Apache-2.0'.

License: LGPL-3.0-or-later
 On Debian systems, the complete text of the GNU Lesser General Public License
 Version 3 can be found in `/usr/share/common-licenses/LGPL-3'.
 Source code for libuiohook is available at: https://github.com/TolikPylypchuk/libuiohook

License: LGPL-2.1-or-later
 On Debian systems, the complete text of the GNU Lesser General Public License
 Version 2.1 can be found in `/usr/share/common-licenses/LGPL-2.1'.

License: BSD-3-Clause
 Redistribution and use in source and binary forms, with or without
 modification, are permitted provided that the following conditions are met:
 .
 1. Redistributions of source code must retain the above copyright notice,
    this list of conditions and the following disclaimer.
 2. Redistributions in binary form must reproduce the above copyright notice,
    this list of conditions and the following disclaimer in the documentation
    and/or other materials provided with the distribution.
 3. Neither the name of the copyright holder nor the names of its contributors
    may be used to endorse or promote products derived from this software
    without specific prior written permission.
 .
 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
 LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 POSSIBILITY OF SUCH DAMAGE.

License: BSD-2-Clause
 Redistribution and use in source and binary forms, with or without
 modification, are permitted provided that the following conditions are met:
 .
 1. Redistributions of source code must retain the above copyright notice,
    this list of conditions and the following disclaimer.
 2. Redistributions in binary form must reproduce the above copyright notice,
    this list of conditions and the following disclaimer in the documentation
    and/or other materials provided with the distribution.
 .
 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
 LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 POSSIBILITY OF SUCH DAMAGE.

License: Old-MIT
 Permission is hereby granted, without written agreement and without license
 or royalty fees, to use, copy, modify, and distribute this software and its
 documentation for any purpose, provided that the above copyright notice and
 the following two paragraphs appear in all copies of this software.
 .
 IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR DIRECT,
 INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES ARISING OUT OF THE
 USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN IF THE COPYRIGHT HOLDER
 HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 .
 THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING, BUT
 NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS ON AN "AS IS"
 BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO PROVIDE MAINTENANCE,
 SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.

License: MPL-2.0
 This Source Code Form is subject to the terms of the Mozilla Public
 License, v. 2.0. If a copy of the MPL was not distributed with this
 file, You can obtain one at https://mozilla.org/MPL/2.0/.

License: MPL-1.1
 The contents of this file are subject to the Mozilla Public License Version
 1.1 (the "License"); you may not use this file except in compliance with
 the License. You may obtain a copy of the License at https://www.mozilla.org/MPL/

License: ImageMagick
 Before we get to the text of the license, lets just review what the license says in simple terms:
 .
 It allows you to:
   * freely download and use ImageMagick software, in whole or in part, for personal, company internal, or commercial purposes;
   * use ImageMagick software in packages or distributions that you create;
   * link against a library under a different license;
   * link code under a different license against a library under this license;
   * merge code into a work under a different license;
   * extend patent grants to any code using code under this license;
   * and extend patent protection.
 .
 It requires you to:
   * include a copy of the license in any redistribution you may make that includes ImageMagick software;
   * provide clear attribution to ImageMagick Studio LLC for any distributions that include ImageMagick software.
EOF

cp "$REPO_ROOT/LICENSE" "$DEB_STAGING/usr/share/doc/jhatpat/"
cp "$REPO_ROOT/THIRD-PARTY-NOTICES.md" "$DEB_STAGING/usr/share/doc/jhatpat/"
cp "$REPO_ROOT/README.md" "$DEB_STAGING/usr/share/doc/jhatpat/"

# Copy Magick.NET delegate Notice.txt if available
MAGICK_NOTICE=$(find ~/.nuget/packages -path "*/magick.net-q8-anycpu/*/Notice.txt" 2>/dev/null | head -n 1)
if [ -n "$MAGICK_NOTICE" ] && [ -f "$MAGICK_NOTICE" ]; then
    cp "$MAGICK_NOTICE" "$DEB_STAGING/usr/share/doc/jhatpat/Magick.NET-Notice.txt"
fi

# Create symlinks in /usr/bin
ln -sf /usr/lib/jhatpat/Filee "$DEB_STAGING/usr/bin/jhatpat"
ln -sf /usr/lib/jhatpat/filee-cli "$DEB_STAGING/usr/bin/jhatpat-cli"
ln -sf /usr/lib/jhatpat/filee-cli "$DEB_STAGING/usr/bin/filee"
ln -sf /usr/lib/jhatpat/filee-cli "$DEB_STAGING/usr/bin/filee-cli"

# Install application icons across standard hicolor resolutions
for size in 16 24 32 48 64 128 256 512; do
    mkdir -p "$DEB_STAGING/usr/share/icons/hicolor/${size}x${size}/apps"
    if [ -f "$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat-${size}.png" ]; then
        cp "$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat-${size}.png" "$DEB_STAGING/usr/share/icons/hicolor/${size}x${size}/apps/jhatpat.png"
    fi
done
mkdir -p "$DEB_STAGING/usr/share/icons/hicolor/scalable/apps"
cp "$ICON_SVG" "$DEB_STAGING/usr/share/icons/hicolor/scalable/apps/jhatpat.svg"

# Install desktop entry
cat << 'EOF' > "$DEB_STAGING/usr/share/applications/com.jhatpat.app.desktop"
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
Actions=Radial;DropZone;Crop;Framing;Resize;Batch;

[Desktop Action Radial]
Name=Quick Actions (Radial Menu)
Exec=jhatpat --radial

[Desktop Action DropZone]
Name=Open Drop Zone
Exec=jhatpat --drop-zone

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
EOF

ln -sf com.jhatpat.app.desktop "$DEB_STAGING/usr/share/applications/jhatpat.desktop"

# Validate desktop file if tool is available
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$DEB_STAGING/usr/share/applications/com.jhatpat.app.desktop"
fi

# Calculate installed size in KB
INSTALLED_SIZE=$(du -sk "$DEB_STAGING/usr" | awk '{print $1}')

# Write DEBIAN/control
cat << EOF > "$DEB_STAGING/DEBIAN/control"
Package: jhatpat
Version: ${VERSION}
Section: graphics
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE}
Maintainer: Jhatpat Developers <info@jhatpat.app>
Depends: libc6 (>= 2.34), libgcc-s1, libfontconfig1, libx11-6, libice6, libsm6
Recommends: imagemagick
Homepage: https://github.com/AsadMokarim/Jhatpat
Description: Image cropping, background framing, resize, compression, and conversion
 Jhatpat is an extensible cross-platform desktop productivity application featuring
 contextual radial workflows, image crop presets, customizable background framing,
 resizing, compression, and batch image processing.
EOF

# Write DEBIAN/postinst
cat << 'EOF' > "$DEB_STAGING/DEBIAN/postinst"
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
    command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
exit 0
EOF
chmod 0755 "$DEB_STAGING/DEBIAN/postinst"

# Write DEBIAN/postrm
cat << 'EOF' > "$DEB_STAGING/DEBIAN/postrm"
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
    command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
exit 0
EOF
chmod 0755 "$DEB_STAGING/DEBIAN/postrm"

DEB_PACKAGE="$OUTPUT_DIR/jhatpat_${VERSION}_amd64.deb"
echo "==> Building Debian package $DEB_PACKAGE..."
dpkg-deb --build --root-owner-group "$DEB_STAGING" "$DEB_PACKAGE"


# 3. Build Portable Tarball (.tar.gz)
echo "==> Constructing portable tarball distribution..."
rm -rf "$REPO_ROOT/build/tarball-staging"
mkdir -p "$TAR_STAGING"

# Copy application files
cp -a "$PUBLISH_DIR/." "$TAR_STAGING/"
# Clean up any development debug symbols or XML documentation files from portable payload
rm -f "$TAR_STAGING/"*.pdb "$TAR_STAGING/"*.xml
cp "$ICON_SVG" "$TAR_STAGING/jhatpat.svg"
cp "$ICON_PNG" "$TAR_STAGING/jhatpat.png"
for size in 16 24 32 48 64 128 256 512; do
    if [ -f "$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat-${size}.png" ]; then
        cp "$REPO_ROOT/src/Filee.App/Assets/Icons/jhatpat-${size}.png" "$TAR_STAGING/jhatpat-${size}.png"
    fi
done
cp "$REPO_ROOT/LICENSE" "$TAR_STAGING/LICENSE"
cp "$REPO_ROOT/THIRD-PARTY-NOTICES.md" "$TAR_STAGING/THIRD-PARTY-NOTICES.md"
cp "$REPO_ROOT/README.md" "$TAR_STAGING/README.md"
if [ -n "$MAGICK_NOTICE" ] && [ -f "$MAGICK_NOTICE" ]; then
    cp "$MAGICK_NOTICE" "$TAR_STAGING/Magick.NET-Notice.txt"
fi
cp "$DEB_STAGING/usr/share/applications/com.jhatpat.app.desktop" "$TAR_STAGING/"

# Add tarball install.sh
cat << 'EOF' > "$TAR_STAGING/install.sh"
#!/usr/bin/env bash
# Standalone installation script for Jhatpat portable distribution
set -euo pipefail

SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(id -u)" -eq 0 ]; then
    INSTALL_DIR="/opt/jhatpat"
    BIN_DIR="/usr/local/bin"
    APP_DIR="/usr/share/applications"
    ICON_DIR="/usr/share/icons/hicolor"
else
    INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/jhatpat"
    BIN_DIR="$HOME/.local/bin"
    APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
fi

echo "Installing Jhatpat to $INSTALL_DIR..."
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APP_DIR" "$ICON_DIR/scalable/apps" "$ICON_DIR/256x256/apps"

# Copy files
cp -a "$SRC_DIR/." "$INSTALL_DIR/"
rm -f "$INSTALL_DIR/install.sh"

chmod +x "$INSTALL_DIR/Filee" "$INSTALL_DIR/filee-cli" "$INSTALL_DIR/uninstall.sh"

# Create symlinks
ln -sf "$INSTALL_DIR/Filee" "$BIN_DIR/jhatpat"
ln -sf "$INSTALL_DIR/filee-cli" "$BIN_DIR/jhatpat-cli"
ln -sf "$INSTALL_DIR/filee-cli" "$BIN_DIR/filee"
ln -sf "$INSTALL_DIR/filee-cli" "$BIN_DIR/filee-cli"

# Icons
for size in 16 24 32 48 64 128 256 512; do
    mkdir -p "$ICON_DIR/${size}x${size}/apps"
    [ -f "$INSTALL_DIR/jhatpat-${size}.png" ] && cp "$INSTALL_DIR/jhatpat-${size}.png" "$ICON_DIR/${size}x${size}/apps/jhatpat.png"
done
mkdir -p "$ICON_DIR/scalable/apps"
cp "$INSTALL_DIR/jhatpat.svg" "$ICON_DIR/scalable/apps/jhatpat.svg"

# Desktop file
cp "$INSTALL_DIR/com.jhatpat.app.desktop" "$APP_DIR/com.jhatpat.app.desktop"
sed -i "s|Exec=jhatpat|Exec=$BIN_DIR/jhatpat|g" "$APP_DIR/com.jhatpat.app.desktop"

# Refresh desktop database
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$APP_DIR" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f "$ICON_DIR" || true

echo "Jhatpat installed successfully!"
echo "Launcher binary: $BIN_DIR/jhatpat"
echo "CLI binary:      $BIN_DIR/jhatpat-cli"
echo "To uninstall, run: $INSTALL_DIR/uninstall.sh"
EOF
chmod +x "$TAR_STAGING/install.sh"

# Add tarball uninstall.sh
cat << 'EOF' > "$TAR_STAGING/uninstall.sh"
#!/usr/bin/env bash
# Standalone uninstallation script for Jhatpat portable distribution
set -euo pipefail

SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(id -u)" -eq 0 ]; then
    INSTALL_DIR="/opt/jhatpat"
    BIN_DIR="/usr/local/bin"
    APP_DIR="/usr/share/applications"
    ICON_DIR="/usr/share/icons/hicolor"
else
    INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/jhatpat"
    BIN_DIR="$HOME/.local/bin"
    APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
fi

echo "Removing Jhatpat from $INSTALL_DIR..."
rm -f "$BIN_DIR/jhatpat" "$BIN_DIR/jhatpat-cli" "$BIN_DIR/filee" "$BIN_DIR/filee-cli"
rm -f "$APP_DIR/com.jhatpat.app.desktop" "$APP_DIR/jhatpat.desktop"
for size in 16 24 32 48 64 128 256 512; do
    rm -f "$ICON_DIR/${size}x${size}/apps/jhatpat.png"
done
rm -f "$ICON_DIR/scalable/apps/jhatpat.svg"
rm -rf "$INSTALL_DIR"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$APP_DIR" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f "$ICON_DIR" || true

echo "Jhatpat was cleanly uninstalled."
EOF
chmod +x "$TAR_STAGING/uninstall.sh"

TAR_PACKAGE="$OUTPUT_DIR/jhatpat-${VERSION}-linux-x64.tar.gz"
echo "==> Creating tarball $TAR_PACKAGE..."
tar -czf "$TAR_PACKAGE" -C "$REPO_ROOT/build/tarball-staging" "jhatpat-$VERSION"


# 4. Verify Built Packages
echo "==> Verifying package integrity..."
echo "--- Debian Package Information ---"
dpkg-deb -I "$DEB_PACKAGE"
echo ""
echo "--- Debian Package Contents Summary ---"
PKG_LISTING="$(mktemp /tmp/jhatpat-deb-listing-XXXXXX)"
dpkg-deb -c "$DEB_PACKAGE" > "$PKG_LISTING"
FILE_COUNT=$(wc -l < "$PKG_LISTING")
echo "Total files/directories in package: $FILE_COUNT"
echo "Sample entries:"
head -n 15 "$PKG_LISTING"
rm -f "$PKG_LISTING"

# Test extraction into temporary staging area
TEST_EXTRACT_DIR="$(mktemp -d /tmp/jhatpat-deb-test-XXXXXX)"
dpkg-deb -x "$DEB_PACKAGE" "$TEST_EXTRACT_DIR"

# Verify binary execution in test extraction
echo "--- Testing extracted CLI binary execution ---"
"$TEST_EXTRACT_DIR/usr/lib/jhatpat/filee-cli" --help | head -n 5
rm -rf "$TEST_EXTRACT_DIR"

# Clean up staging directories
rm -rf "$DEB_STAGING" "$REPO_ROOT/build/tarball-staging"

echo "============================================================"
echo "Package build and verification successful!"
echo "Debian Package:  $DEB_PACKAGE ($(du -h "$DEB_PACKAGE" | cut -f1))"
echo "Tarball Archive: $TAR_PACKAGE ($(du -h "$TAR_PACKAGE" | cut -f1))"
echo "SHA256 Checksums:"
sha256sum "$DEB_PACKAGE" "$TAR_PACKAGE"
echo "============================================================"
