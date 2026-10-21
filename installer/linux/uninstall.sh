#!/bin/sh
set -eu

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/jhatpat/application"
desktop_file="$data_dir/applications/com.jhatpat.app.desktop"
python3 - "$data_dir" "$install_dir" <<'PY'
import pathlib
import sys

root, target = map(pathlib.Path, sys.argv[1:])
expected = root.resolve() / 'jhatpat' / 'application'
if target.is_symlink() or target.resolve() != expected:
    raise SystemExit('Refusing to remove an installation outside its expected directory.')
if not (target / '.jhatpat-package').exists() or (target / '.jhatpat-package').read_text(encoding='utf-8').strip() != 'com.jhatpat.app':
    raise SystemExit('The directory is not an installation created by Jhatpat.')
PY
"$install_dir/Filee" --uninstall-cleanup || true
if [ -L "$HOME/.local/bin/jhatpat" ] && [ "$(readlink "$HOME/.local/bin/jhatpat")" = "$install_dir/Filee" ]; then
    rm -- "$HOME/.local/bin/jhatpat"
fi
if [ -L "$HOME/.local/bin/jhatpat-cli" ] && [ "$(readlink "$HOME/.local/bin/jhatpat-cli")" = "$install_dir/filee-cli" ]; then
    rm -- "$HOME/.local/bin/jhatpat-cli"
fi
if [ -f "$desktop_file" ] && grep -q '^X-Jhatpat-Managed=true$' "$desktop_file"; then
    rm -- "$desktop_file"
fi
rm -rf -- "$install_dir"
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$data_dir/applications" || true
fi
printf '%s\n' 'Jhatpat was removed. Your settings and conversion results were kept.'
