#!/usr/bin/env bash
#
# Installs, upgrades or uninstalls the TrakHound CLI ('th').
# Run it from the extracted release folder: sudo bash install.sh [options]
#
# The release includes the .NET Runtime, so 'th' runs without .NET installed. The commands that build projects
# (ex. 'th packages build' and 'th dev run') also need the .NET SDK.

set -euo pipefail

readonly PROGRAM_DIR="/opt/trakhound/cli"
readonly EXECUTABLE="th"
readonly LINK_PATH="/usr/local/bin/th"
readonly CONFIGURATION_FILE="cli.config.yaml"

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

uninstall=0


usage() {
    cat <<EOF
Usage: sudo bash install.sh [options]

Installs the TrakHound CLI to $PROGRAM_DIR and links $LINK_PATH to it, or upgrades it.

Options:
  --uninstall   Remove the program files and $LINK_PATH. Each user's ~/.trakhound folder is kept
  -h, --help    Show this help

An upgrade keeps $PROGRAM_DIR/$CONFIGURATION_FILE if it was changed. Settings for one user
go in ~/.trakhound/$CONFIGURATION_FILE instead.
EOF
}

info() { echo "==> $*"; }
fail() { echo "Error: $*" >&2; exit 1; }


# Read Options
while [[ $# -gt 0 ]]; do
    case "$1" in
        --uninstall) uninstall=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; echo >&2; fail "Unknown option '$1'" ;;
    esac
done

[[ $EUID -eq 0 ]] || fail "Run as root (ex. sudo bash install.sh)"


# Remove the link only when it points to this install, so a 'th' installed another way is left alone
remove_link() {
    if [[ -L "$LINK_PATH" && "$(readlink "$LINK_PATH")" == "$PROGRAM_DIR/$EXECUTABLE" ]]; then
        rm -f "$LINK_PATH"
    fi
}

if [[ $uninstall -eq 1 ]]; then
    remove_link
    if [[ -d "$PROGRAM_DIR" ]]; then
        info "Removing $PROGRAM_DIR"
        rm -rf "$PROGRAM_DIR"
        rmdir --ignore-fail-on-non-empty "$(dirname "$PROGRAM_DIR")"
    fi
    echo "TrakHound CLI uninstalled."
    exit 0
fi


# Check the Release
[[ -f "$SOURCE_DIR/$EXECUTABLE" ]] || fail "$EXECUTABLE was not found next to install.sh. Run install.sh from the extracted release folder"
[[ "$SOURCE_DIR" != "$PROGRAM_DIR" ]] || fail "Run install.sh from the extracted release folder, not from $PROGRAM_DIR"

if [[ -e "$LINK_PATH" || -L "$LINK_PATH" ]] && [[ "$(readlink "$LINK_PATH" 2>/dev/null || true)" != "$PROGRAM_DIR/$EXECUTABLE" ]]; then
    fail "$LINK_PATH already exists and isn't this install's link. Remove it (or the other 'th' it belongs to), then run install.sh again"
fi

# The build must match the machine. The ELF header's machine field is at offset 18
machine="$(uname -m)"
elf_machine="$(od -An -tx1 -j18 -N1 "$SOURCE_DIR/$EXECUTABLE" | tr -d ' ')"
case "$machine:$elf_machine" in
    x86_64:3e|aarch64:b7|arm64:b7) ;;
    *) fail "This release isn't built for this machine ($machine). Use the linux-x64 release for x86_64, or linux-arm64 for aarch64" ;;
esac

# .NET needs ICU for globalization, and exits at startup without it (grep reads all of the input, since an early exit would fail the pipe)
if command -v ldconfig >/dev/null 2>&1 && ! ldconfig -p | grep 'libicuuc\.so' >/dev/null; then
    fail "ICU was not found. Install your distribution's libicu package (ex. libicu74 on Ubuntu 24.04, libicu72 on Debian 12, libicu on RHEL or Fedora), then run install.sh again"
fi


upgrade=0
if [[ -f "$PROGRAM_DIR/$EXECUTABLE" ]]; then upgrade=1; fi

if [[ $upgrade -eq 1 ]]; then
    info "Upgrading the TrakHound CLI in $PROGRAM_DIR"
else
    info "Installing the TrakHound CLI to $PROGRAM_DIR"
fi


# Keep a changed Configuration File
kept_configuration=""
if [[ -f "$PROGRAM_DIR/$CONFIGURATION_FILE" ]] && ! cmp -s "$PROGRAM_DIR/$CONFIGURATION_FILE" "$SOURCE_DIR/$CONFIGURATION_FILE"; then
    kept_configuration="$(mktemp)"
    cp "$PROGRAM_DIR/$CONFIGURATION_FILE" "$kept_configuration"
fi


# Replace the Program Files (so files from an earlier version don't stay behind)
rm -rf "$PROGRAM_DIR"
mkdir -p "$PROGRAM_DIR"
cp -R "$SOURCE_DIR/." "$PROGRAM_DIR/"

if [[ -n "$kept_configuration" ]]; then
    cp "$kept_configuration" "$PROGRAM_DIR/$CONFIGURATION_FILE"
    rm -f "$kept_configuration"
    info "Kept the changed $PROGRAM_DIR/$CONFIGURATION_FILE"
fi

# Owned by root, so users can run 'th' but not change it
chown -R root:root "$PROGRAM_DIR"
chmod -R a+rX,go-w "$PROGRAM_DIR"
chmod 755 "$PROGRAM_DIR/$EXECUTABLE"


# Put 'th' on the PATH
mkdir -p "$(dirname "$LINK_PATH")"
ln -sfn "$PROGRAM_DIR/$EXECUTABLE" "$LINK_PATH"


# Summary
echo
if [[ $upgrade -eq 1 ]]; then echo "TrakHound CLI upgraded."; else echo "TrakHound CLI installed."; fi
echo "  Run      : th help"
echo "  Settings : ~/.trakhound/$CONFIGURATION_FILE (or $PROGRAM_DIR/$CONFIGURATION_FILE for all users)"
if ! command -v dotnet >/dev/null 2>&1; then
    echo
    echo "The commands that build projects (ex. 'th packages build' and 'th dev run') also need the .NET SDK, which wasn't found on the PATH."
fi
