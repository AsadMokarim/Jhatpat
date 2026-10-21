#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
install_dir="$HOME/Applications/Jhatpat.app"
bin_dir="$HOME/.local/bin"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run uninstall.sh first."
    exit 1
fi
if [ -e "$bin_dir/jhatpat" ] || [ -L "$bin_dir/jhatpat" ]; then
    printf '%s\n' "$bin_dir/jhatpat already exists. Remove or rename it before installing Jhatpat."
    exit 1
fi
test -x "$source_dir/Jhatpat.app/Contents/MacOS/Filee"
mkdir -p "$HOME/Applications" "$bin_dir"
/usr/bin/ditto "$source_dir/Jhatpat.app" "$install_dir"
ln -s "$install_dir/Contents/MacOS/Filee" "$bin_dir/jhatpat"
ln -s "$install_dir/Contents/MacOS/filee-cli" "$bin_dir/jhatpat-cli"
printf '%s\n' "Installed $install_dir" "Commands: $bin_dir/jhatpat and $bin_dir/jhatpat-cli (add $bin_dir to PATH if needed)."
printf '%s\n' 'Open Jhatpat from Applications. Grant Accessibility and Finder Automation access when requested.'
