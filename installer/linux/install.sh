#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/jhatpat/application"
bin_dir="$HOME/.local/bin"
desktop_file="$data_dir/applications/com.jhatpat.app.desktop"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run its uninstall.sh first."
    exit 1
fi
if [ -e "$bin_dir/jhatpat" ] || [ -L "$bin_dir/jhatpat" ]; then
    printf '%s\n' "$bin_dir/jhatpat already exists. Remove or rename it before installing Jhatpat."
    exit 1
fi
if [ -e "$desktop_file" ] || [ -L "$desktop_file" ]; then
    printf '%s\n' "$desktop_file already exists. Remove or rename it before installing Jhatpat."
    exit 1
fi
command -v python3 >/dev/null
test -x "$source_dir/Filee"
test -x "$source_dir/filee-cli"
mkdir -p "$data_dir/jhatpat" "$bin_dir" "$data_dir/applications"
cp -R "$source_dir" "$install_dir"
printf '%s\n' 'com.jhatpat.app' > "$install_dir/.jhatpat-package"
ln -s "$install_dir/Filee" "$bin_dir/jhatpat"
ln -s "$install_dir/filee-cli" "$bin_dir/jhatpat-cli"
python3 - "$install_dir" "$desktop_file" "$bin_dir" <<'PY'
import pathlib
import sys

app, desktop, bin_dir = map(pathlib.Path, sys.argv[1:])
def quoted_exec(value):
    value = value.replace('%', '%%')
    for character in ('\\', '"', '`', '$'):
        value = value.replace(character, '\\' + character)
    return '"' + value.replace('\\', '\\\\').replace('\n', '\\n').replace('\r', '\\r') + '"'

def desktop_value(value):
    return str(value).replace('\\', '\\\\').replace('\n', '\\n').replace('\r', '\\r')

icon_path = app / "jhatpat.png"
if not icon_path.exists():
    icon_path = app / "jhatpat.svg"

desktop.write_text(
    '[Desktop Entry]\nType=Application\nName=Jhatpat\n'
    'Comment=Image crop presets, background framing, resize, compression, and conversion\n'
    f'Exec={quoted_exec(str(app / "Filee"))} --convert %F\n'
    f'Icon={desktop_value(icon_path)}\n'
    'Terminal=false\nCategories=Graphics;Utility;\nX-Jhatpat-Managed=true\n'
    'Actions=Radial;DropZone;\n\n'
    '[Desktop Action Radial]\nName=Quick Actions (Radial Menu)\n'
    f'Exec={quoted_exec(str(bin_dir / "jhatpat"))} --radial\n\n'
    '[Desktop Action DropZone]\nName=Open Drop Zone\n'
    f'Exec={quoted_exec(str(bin_dir / "jhatpat"))} --drop-zone\n', encoding='utf-8')
PY
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$data_dir/applications"
fi
printf '%s\n' "Installed Jhatpat at $install_dir" "Commands: $bin_dir/jhatpat and $bin_dir/jhatpat-cli (add $bin_dir to PATH if needed)."
