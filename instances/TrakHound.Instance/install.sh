#!/usr/bin/env bash
#
# Installs, upgrades or uninstalls the TrakHound Instance as a systemd service (see the Installation Guide, section 5).
# Run it from the extracted release folder: sudo bash install.sh [options]
#
# An upgrade replaces only the program files. The configuration, packages and volumes in the data folder are kept.

set -euo pipefail

readonly SERVICE_NAME="trakhound-instance"
readonly SERVICE_USER="trakhound"
readonly PROGRAM_DIR="/opt/trakhound/instance"
readonly DATA_ROOT="/var/lib/trakhound"
readonly DATA_DIR="$DATA_ROOT/instance"
readonly CONFIGURATION_DIR="$DATA_DIR/config"
readonly PROFILE_DIR="$CONFIGURATION_DIR/default"
readonly UNIT_PATH="/etc/systemd/system/$SERVICE_NAME.service"
readonly EXECUTABLE="TrakHound.Instance"
readonly DEFAULT_PORT=8472

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

port=""
admin_user=""
admin_password_file=""
deployment=""
start=1
uninstall=0
purge=0


usage() {
    cat <<EOF
Usage: sudo bash install.sh [options]

Installs the TrakHound Instance as the systemd service '$SERVICE_NAME', or upgrades it.

Options:
  --port <port>                 HTTP port for a new install (default $DEFAULT_PORT). An upgrade keeps the configured port
  --admin-user <name>           Create the admin account, or replace it (ex. to reset a forgotten password)
  --admin-password-file <file>  Read the admin password from the first line of a file instead of asking for it
  --deployment <file.thd>       A deployment to install when the Instance starts
  --no-start                    Install without starting the service
  --uninstall                   Stop and remove the service and the program files. The data folder is kept
  --purge                       With --uninstall, also delete the data folder and the '$SERVICE_USER' user
  -h, --help                    Show this help

Locations:
  Program files  $PROGRAM_DIR
  Data           $DATA_DIR (configuration, packages and volumes)
EOF
}

info() { echo "==> $*"; }
warn() { echo "Warning: $*" >&2; }
fail() { echo "Error: $*" >&2; exit 1; }


# Read Options
while [[ $# -gt 0 ]]; do
    case "$1" in
        --port) [[ $# -ge 2 ]] || fail "--port needs a value"; port="$2"; shift 2 ;;
        --admin-user) [[ $# -ge 2 ]] || fail "--admin-user needs a value"; admin_user="$2"; shift 2 ;;
        --admin-password-file) [[ $# -ge 2 ]] || fail "--admin-password-file needs a value"; admin_password_file="$2"; shift 2 ;;
        --deployment) [[ $# -ge 2 ]] || fail "--deployment needs a value"; deployment="$2"; shift 2 ;;
        --no-start) start=0; shift ;;
        --uninstall) uninstall=1; shift ;;
        --purge) purge=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; echo >&2; fail "Unknown option '$1'" ;;
    esac
done

[[ $EUID -eq 0 ]] || fail "Run as root (ex. sudo bash install.sh)"
command -v systemctl >/dev/null 2>&1 || fail "systemd is required (systemctl was not found)"


# Run a command as the service user (so the files it writes have the right owner)
run_as_service_user() {
    if command -v runuser >/dev/null 2>&1; then
        runuser -u "$SERVICE_USER" -- "$@"
    else
        sudo -u "$SERVICE_USER" -- "$@"
    fi
}

uninstall() {
    if [[ -f "$UNIT_PATH" ]]; then
        info "Stopping and removing the $SERVICE_NAME service"
        systemctl disable --now "$SERVICE_NAME" || true
        rm -f "$UNIT_PATH"
        rm -rf "/etc/systemd/system/$SERVICE_NAME.service.d"
        systemctl daemon-reload
    fi

    if [[ -d "$PROGRAM_DIR" ]]; then
        info "Removing $PROGRAM_DIR"
        rm -rf "$PROGRAM_DIR"
        rmdir --ignore-fail-on-non-empty "$(dirname "$PROGRAM_DIR")"
    fi

    if [[ $purge -eq 1 ]]; then
        info "Removing $DATA_ROOT and the '$SERVICE_USER' user"
        rm -rf "$DATA_ROOT"
        if id "$SERVICE_USER" >/dev/null 2>&1; then userdel "$SERVICE_USER" || warn "Could not remove the '$SERVICE_USER' user"; fi
    else
        echo "The data in $DATA_DIR was kept. Run with --uninstall --purge to delete it."
    fi
}

if [[ $uninstall -eq 1 ]]; then
    uninstall
    exit 0
fi

[[ $purge -eq 0 ]] || fail "--purge is only used with --uninstall"


# Check the Options
[[ -f "$SOURCE_DIR/$EXECUTABLE" ]] || fail "$EXECUTABLE was not found next to install.sh. Run install.sh from the extracted release folder"
[[ -f "$SOURCE_DIR/$SERVICE_NAME.service" ]] || fail "$SERVICE_NAME.service was not found next to install.sh"
[[ "$SOURCE_DIR" != "$PROGRAM_DIR" ]] || fail "Run install.sh from the extracted release folder, not from $PROGRAM_DIR"

if [[ -n "$port" ]]; then
    if [[ ! "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then fail "--port must be a number from 1 to 65535"; fi
fi
if [[ -n "$admin_password_file" ]]; then
    [[ -n "$admin_user" ]] || fail "--admin-password-file is used with --admin-user"
    [[ -r "$admin_password_file" ]] || fail "Can't read the password file '$admin_password_file'"
fi
if [[ -n "$deployment" ]]; then
    [[ -f "$deployment" ]] || fail "The deployment file '$deployment' was not found"
    [[ "$deployment" == *.thd ]] || fail "The deployment file must have the .thd extension"
fi

# The build must match the machine (a mismatch fails later with status=203/EXEC). The ELF header's machine field is at offset 18
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
if [[ -f "$UNIT_PATH" || -f "$PROGRAM_DIR/$EXECUTABLE" ]]; then upgrade=1; fi

if [[ $upgrade -eq 1 ]]; then
    info "Upgrading the TrakHound Instance in $PROGRAM_DIR"
else
    info "Installing the TrakHound Instance to $PROGRAM_DIR"
fi


# Create the Service User
if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    info "Creating the '$SERVICE_USER' user"
    nologin="$(command -v nologin || echo /usr/sbin/nologin)"
    useradd --system --home-dir "$DATA_ROOT" --no-create-home --shell "$nologin" "$SERVICE_USER"
fi


# Stop the Service before replacing its files (systemd waits for the Instance to stop, up to TimeoutStopSec)
was_active=0
if systemctl is-active --quiet "$SERVICE_NAME"; then
    was_active=1
    info "Stopping the $SERVICE_NAME service"
    systemctl stop "$SERVICE_NAME"
fi


# Copy the Program Files
mkdir -p "$PROGRAM_DIR"
cp -R "$SOURCE_DIR/." "$PROGRAM_DIR/"
chmod +x "$PROGRAM_DIR/$EXECUTABLE"

# The Instance writes everything (logs, temporary files and backups too) in its Data Folder, so the program files stay owned by
# root and the service user can't change them
chown -R root:root "$PROGRAM_DIR"
chmod -R a+rX,go-w "$PROGRAM_DIR"


# Create the Data Folder (only the service user can read it: the keys that protect sign-in cookies are stored unencrypted on Linux)
mkdir -p "$PROFILE_DIR" "$DATA_DIR/packages" "$DATA_DIR/volumes"
chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_ROOT"
chmod 700 "$DATA_DIR"


# Set the Port for a new Instance. The Instance reads the rest of its settings from their defaults and keeps this file
configuration_file="$PROFILE_DIR/instance.config.yaml"
if [[ -n "$port" ]]; then
    if [[ -f "$configuration_file" ]]; then
        warn "--port is ignored because $configuration_file already exists. Change the port in the Admin interface (Settings > Interfaces)"
    else
        instance_id="i-$(tr -dc 'a-z0-9' </dev/urandom | head -c 10 || true)"
        printf 'instanceId: %s\nhttpPort: %s\n' "$instance_id" "$port" > "$configuration_file"
        chown "$SERVICE_USER:$SERVICE_USER" "$configuration_file"
        chmod 600 "$configuration_file"
    fi
fi


# Copy the Deployment (the Instance installs it the next time it starts)
if [[ -n "$deployment" ]]; then
    info "Copying the deployment $(basename "$deployment")"
    mkdir -p "$DATA_DIR/deployments"
    cp "$deployment" "$DATA_DIR/deployments/"
    chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR/deployments"
fi


# Create the Admin Account
set_admin() {
    local user="$1"
    info "Setting the admin account '$user'"
    if [[ -n "$admin_password_file" ]]; then
        run_as_service_user "$PROGRAM_DIR/$EXECUTABLE" set-admin "$user" --configurationPath "$CONFIGURATION_DIR" < "$admin_password_file"
    else
        run_as_service_user "$PROGRAM_DIR/$EXECUTABLE" set-admin "$user" --configurationPath "$CONFIGURATION_DIR"
    fi
}

if [[ -n "$admin_user" ]]; then
    set_admin "$admin_user"
elif ! compgen -G "$PROFILE_DIR/security/admins/*.yaml" > /dev/null; then
    if [[ -t 0 ]]; then
        echo
        echo "Create the admin account for the Admin interface."
        read -r -p "Admin username [admin]: " admin_user
        set_admin "${admin_user:-admin}"
    else
        warn "No admin account exists. Create one with: sudo -u $SERVICE_USER $PROGRAM_DIR/$EXECUTABLE set-admin <username> --configurationPath $CONFIGURATION_DIR"
    fi
fi


# Install the Service. Customize it with 'systemctl edit $SERVICE_NAME', since this file is replaced on every upgrade
info "Installing the $SERVICE_NAME service"
cp "$PROGRAM_DIR/$SERVICE_NAME.service" "$UNIT_PATH"
chmod 644 "$UNIT_PATH"
systemctl daemon-reload
systemctl enable --quiet "$SERVICE_NAME"

if [[ $start -eq 1 ]]; then
    info "Starting the $SERVICE_NAME service (this can take a minute the first time)"
    if ! systemctl start "$SERVICE_NAME"; then
        systemctl status "$SERVICE_NAME" --no-pager --lines=20 || true
        fail "The service didn't start. See: journalctl -u $SERVICE_NAME"
    fi
elif [[ $was_active -eq 1 ]]; then
    warn "The service was running before the upgrade and is now stopped (--no-start)"
fi


# Summary
configured_port="$(sed -n 's/^httpPort:[[:space:]]*\([0-9]*\).*/\1/p' "$configuration_file" 2>/dev/null | head -n 1 || true)"
configured_port="${configured_port:-$DEFAULT_PORT}"

echo
if [[ $upgrade -eq 1 ]]; then echo "TrakHound Instance upgraded."; else echo "TrakHound Instance installed."; fi
echo "  Admin interface : http://localhost:$configured_port/_admin"
echo "  Status          : systemctl status $SERVICE_NAME"
echo "  Logs            : journalctl -u $SERVICE_NAME -f"
if [[ $upgrade -eq 0 ]]; then
    echo
    echo "To reach the Instance from other computers, open port $configured_port in the firewall, or put a reverse proxy in front of it (Installation Guide, section 5)."
fi
