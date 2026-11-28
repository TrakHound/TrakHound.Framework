#!/bin/sh
# TrakHound Instance Docker Entrypoint
#
# Copies the Default Packages to the Package directory when it has no packages yet (a new volume), like the
# Windows installer does on new installs, then runs the Instance with the container's arguments
# (ex. 'docker exec -it <container> /opt/trakhound/docker-entrypoint.sh set-admin admin', which asks for the password)
#
# TRAKHOUND_DEFAULT_PACKAGES=false (or no, 0) starts without them, like the installer's /DEFAULTPACKAGES=no
#
# TRAKHOUND_DEV_PACKAGES=true (or yes, 1) serves a Dev Package Server for pushing packages being developed (see below)
#
# TRAKHOUND_INSTANCE_NAME and TRAKHOUND_INSTANCE_DESCRIPTION set the Instance's Name and Description (see below)

set -e

packagesFile="$TRAKHOUND_PACKAGE_PATH/trakhound-packages.json"
defaultPackages=/opt/trakhound/default-packages
defaultConfiguration=/opt/trakhound/default-configuration
profileDirectory="$TRAKHOUND_CONFIGURATION_PATH/${TRAKHOUND_CONFIGURATION_PROFILE:-default}"

installDefaultPackages=1
case "$(printf '%s' "$TRAKHOUND_DEFAULT_PACKAGES" | tr '[:upper:]' '[:lower:]')" in
    false|no|0) installDefaultPackages=0 ;;
esac

if [ -n "$TRAKHOUND_PACKAGE_PATH" ] && [ ! -f "$packagesFile" ] && [ -f "$defaultPackages/trakhound-packages.json" ]; then
    if [ "$installDefaultPackages" = "1" ]; then
        echo "Installing Default Packages to $TRAKHOUND_PACKAGE_PATH"
        mkdir -p "$TRAKHOUND_PACKAGE_PATH"
        cp -R "$defaultPackages/." "$TRAKHOUND_PACKAGE_PATH/"

        # Their Configurations (ex. the Welcome App's route), as the Windows installer adds them. A file that's already in the
        # Configuration Profile (ex. from a mounted configuration) is kept
        if [ -n "$TRAKHOUND_CONFIGURATION_PATH" ] && [ -d "$defaultConfiguration" ]; then
            (cd "$defaultConfiguration" && find . -type f) | while IFS= read -r file; do
                if [ ! -e "$profileDirectory/$file" ]; then
                    mkdir -p "$(dirname "$profileDirectory/$file")"
                    cp "$defaultConfiguration/$file" "$profileDirectory/$file"
                fi
            done
        fi
    else
        echo "Default Packages not installed (TRAKHOUND_DEFAULT_PACKAGES=$TRAKHOUND_DEFAULT_PACKAGES)"
    fi
fi

# Dev Package Server : TRAKHOUND_DEV_PACKAGES=true installs TrakHound.Packages.Dev.Api (included in the image) and serves it at
# /packages, so packages being developed can be pushed to the container ('th packages push') and installed from Packages > Browse,
# and adds a 'Development' Package Source for the Instance itself, and a Security Profile ('dev-system-information') that makes the read-only
# System Information the Welcome page shows public. Anyone who can reach the container can upload (installing still needs an
# Admin), so publish the port on the host's loopback address only (ex. '-p 127.0.0.1:8472:8472'). TRAKHOUND_DEV_UPLOAD_TOKEN
# (at least 16 characters) requires that token for uploads instead (read from the environment, never written to the volume), and
# also enables it. The configuration is written on each start, and it and the 'Development' Package Source are removed when the container
# starts without either variable, so it's only served (and listed) while one is set. The package and the packages pushed to it stay
# in the volume
devPackages=/opt/trakhound/dev-packages
devApiConfiguration="$profileDirectory/api/trakhound-packages-dev-api.yaml"
devSecurityProfile="$profileDirectory/security/dev-system-information.config.yaml"
remotesFile="$profileDirectory/remotes.config.yaml"
devPort=$(printf '%s' "${ASPNETCORE_HTTP_PORTS:-8472}" | cut -d';' -f1 | cut -d',' -f1)

# Writes the Package Sources file ($1) to $2 without the Development Package Source added below (id dev, at this container's own
# address), keeping every other Package Source as it is
remove_dev_remote() {
    awk '
        function flush() { if (block != "" && !(isDev && isLocal)) printf "%s", block; block = ""; isDev = 0; isLocal = 0 }
        /^- / { flush(); inBlock = 1; isDev = ($0 == "- id: dev") }
        /^[^ -]/ { flush(); inBlock = 0 }
        {
            if (inBlock) { block = block $0 "\n"; if ($0 ~ /^  baseUrl: http:\/\/localhost:[0-9]+$/) isLocal = 1 }
            else print
        }
        END { flush() }
    ' "$1" > "$2"
}

devRequested=0
case "$(printf '%s' "$TRAKHOUND_DEV_PACKAGES" | tr '[:upper:]' '[:lower:]')" in
    true|yes|1) devRequested=1 ;;
esac
[ -n "$TRAKHOUND_DEV_UPLOAD_TOKEN" ] && devRequested=1

devEnabled=0
if [ "$devRequested" = "1" ]; then
    if [ -n "$TRAKHOUND_DEV_UPLOAD_TOKEN" ] && [ "${#TRAKHOUND_DEV_UPLOAD_TOKEN}" -lt 16 ]; then
        echo "WARNING : TRAKHOUND_DEV_UPLOAD_TOKEN is shorter than 16 characters, so the Dev Package Server isn't enabled. Use a long random value (ex. 'openssl rand -hex 24'), or leave it out" >&2
    elif [ ! -d "$devPackages/api/TrakHound.Packages.Dev.Api" ]; then
        echo "WARNING : The Dev Package Server isn't enabled : this image doesn't include TrakHound.Packages.Dev.Api" >&2
    elif [ -z "$TRAKHOUND_PACKAGE_PATH" ] || [ -z "$TRAKHOUND_CONFIGURATION_PATH" ]; then
        echo "WARNING : The Dev Package Server isn't enabled : TRAKHOUND_PACKAGE_PATH or TRAKHOUND_CONFIGURATION_PATH isn't set" >&2
    else
        devEnabled=1

        # A version already in the volume (ex. updated from a Package Source) is kept
        for package in "$devPackages"/*/*; do
            [ -d "$package" ] || continue
            category=$(basename "$(dirname "$package")")
            if [ ! -d "$TRAKHOUND_PACKAGE_PATH/$category/$(basename "$package")" ]; then
                mkdir -p "$TRAKHOUND_PACKAGE_PATH/$category"
                cp -R "$package" "$TRAKHOUND_PACKAGE_PATH/$category/"
            fi
        done

        # Written on each start, since what uploads need depends on the variables
        if [ -n "$TRAKHOUND_DEV_UPLOAD_TOKEN" ]; then
            devParameters='  uploadToken: ${env:TRAKHOUND_DEV_UPLOAD_TOKEN}'
        else
            devParameters='  allowAnonymousUploads: true'
        fi
        mkdir -p "$(dirname "$devApiConfiguration")"
        cat > "$devApiConfiguration" <<EOF
id: trakhound-packages-dev-api
name: Dev Package Server
description: 'Written when the container starts with TRAKHOUND_DEV_PACKAGES (or TRAKHOUND_DEV_UPLOAD_TOKEN), and removed when it starts without them'
route: packages
volumeId:
routerId:
groupId:
packageId: TrakHound.Packages.Dev.Api
packageVersion: '*'
httpEnabled: true
mcpTools: Marked
mcpReadOnly: true
parameters:
$devParameters
EOF

        # The Package Source for the Instance itself, named Development and listed first, so Packages opens on it (the ID and
        # Organization stay 'dev' and 'Dev', which publish files and 'th packages push --organization Dev' use). One added by an earlier
        # image (named Dev, or not first) is replaced. Without a Package Sources file the Instance writes its default (TrakHound Public),
        # so it's written here with both
        devRemote="- id: dev
  name: Development
  baseUrl: http://localhost:$devPort
  organization: Dev
  accessTokenName:
  accessTokenCode:
  packageManager: false"

        if [ ! -f "$remotesFile" ]; then
            mkdir -p "$profileDirectory"
            printf 'remotes:\n%s\n- id: public\n  name: TrakHound Public\n  baseUrl: https://packages.trakhound.com\n  organization: Public\n  accessTokenName: \n  accessTokenCode: \n  packageManager: false\n' "$devRemote" > "$remotesFile"
        else
            remotesTemp="$remotesFile.tmp"
            remove_dev_remote "$remotesFile" "$remotesTemp.others"
            if grep -q '^- ' "$remotesTemp.others"; then
                # First, right after 'remotes:'
                awk -v dev="$devRemote" '{ print } /^remotes:[[:space:]]*$/ && !done { print dev; done = 1 }' "$remotesTemp.others" > "$remotesTemp"
            else
                # ex. 'remotes: []'
                printf 'remotes:\n%s\n' "$devRemote" > "$remotesTemp"
            fi
            rm -f "$remotesTemp.others"

            if cmp -s "$remotesFile" "$remotesTemp"; then
                rm -f "$remotesTemp"
            else
                mv -f "$remotesTemp" "$remotesFile"
            fi
        fi

        # The System Information the Welcome page reads, public so it shows what is installed from the host's browser (a request through
        # a Docker port mapping comes from another machine, so System Resources that no Profile covers are refused). A Profile without
        # an Identity Provider allows every operation on its Resources, so it only has the ones that can't change anything (not
        # system:apps, system:functions or system:services)
        mkdir -p "$(dirname "$devSecurityProfile")"
        cat > "$devSecurityProfile" <<EOF
id: dev-system-information
description: 'The System Information the Welcome page reads, public for development. Written when the container starts with TRAKHOUND_DEV_PACKAGES (or TRAKHOUND_DEV_UPLOAD_TOKEN), and removed when it starts without them'
priority: 0
providerId:
assignments:
- id: api-catalog
  resourceId: system:api-catalog
  roles: ['*']
  permissions: [read]
- id: instance
  resourceId: system:instance
  roles: ['*']
  permissions: [read]
- id: api-information
  resourceId: system:api-information
  roles: ['*']
  permissions: [read]
- id: modules
  resourceId: system:modules
  roles: ['*']
  permissions: [read]
EOF

        if [ -n "$TRAKHOUND_DEV_UPLOAD_TOKEN" ]; then
            echo "Dev Package Server enabled at /packages (Package Source 'Development'). Push with TRAKHOUND_API_TOKEN set to TRAKHOUND_DEV_UPLOAD_TOKEN : th packages push <file.thp> -s http://<host>:<published port> --organization Dev"
        else
            echo "Dev Package Server enabled at /packages (Package Source 'Development') : th packages push <file.thp> -s http://localhost:<published port> --organization Dev"
            echo "WARNING : Anyone who can reach this container's port can upload packages to the Dev Package Server. Publish the port on the host's loopback address only (ex. '-p 127.0.0.1:8472:8472'), or set TRAKHOUND_DEV_UPLOAD_TOKEN" >&2
        fi
        echo "System Information (system:api-catalog, system:instance, system:api-information, system:modules) is public for the Welcome page (Security Profile 'dev-system-information')"
    fi
fi

if [ "$devEnabled" = "0" ]; then
    devRemoved=0
    if [ -f "$devApiConfiguration" ]; then
        rm -f "$devApiConfiguration"
        devRemoved=1
    fi
    if [ -f "$devSecurityProfile" ]; then
        rm -f "$devSecurityProfile"
        devRemoved=1
    fi

    # The 'Development' Package Source added above (id dev, at this container's own address), so the Instance doesn't keep a source
    # whose server isn't served. Other Package Sources are kept as they are
    if [ -f "$remotesFile" ] && grep -q '^- id: dev$' "$remotesFile"; then
        remotesTemp="$remotesFile.tmp"
        remove_dev_remote "$remotesFile" "$remotesTemp"

        # Without any Package Source left, an empty list (the Instance would write its default for a missing one)
        if ! grep -q '^- ' "$remotesTemp"; then
            printf 'remotes: []\n' > "$remotesTemp"
        fi

        if cmp -s "$remotesFile" "$remotesTemp"; then
            rm -f "$remotesTemp"
        else
            mv -f "$remotesTemp" "$remotesFile"
            devRemoved=1
        fi
    fi

    if [ "$devRemoved" = "1" ]; then
        echo "Dev Package Server, its 'Development' Package Source and the public System Information removed (TRAKHOUND_DEV_PACKAGES isn't set, or TRAKHOUND_DEV_UPLOAD_TOKEN isn't valid)"
    fi
fi

# Instance Name and Description : TRAKHOUND_INSTANCE_NAME and TRAKHOUND_INSTANCE_DESCRIPTION set them (ex. to the container's
# name, from a script), as references to the variables ('${env:...}'), so the value is read when the Instance starts and Settings
# shows where it comes from. While a variable is set it replaces the setting on each start; when it's no longer set, a setting
# that references it is removed, so the Instance uses its default again (its ID, and no Description)
instanceConfigurationFile="$profileDirectory/instance.config.yaml"

# Writes the Instance Settings file ($1, or none) to $2 with the setting $3 (a top level key) referencing the Environment Variable
# $4 when $5 is 1, or without the setting when $5 is 0 and it references that variable. Other settings are kept as they are
set_instance_reference() {
    awk -v key="$3" -v reference="\${env:$4}" -v set="$5" '
        skipping && /^[ \t]/ { next }
        { skipping = 0 }
        index($0, key ":") == 1 {
            value = substr($0, length(key) + 2)
            gsub(/^[ \t]+|[ \t]+$/, "", value)
            if (value ~ /^'\''.*'\''$/ || value ~ /^".*"$/) value = substr(value, 2, length(value) - 2)
            if (set == "1" || value == reference) {
                # Its continuation lines (ex. a multi-line Description) go with it
                skipping = 1
                if (set == "1" && !written) { print key ": '\''" reference "'\''"; written = 1 }
                next
            }
        }
        { print }
        END { if (set == "1" && !written) print key ": '\''" reference "'\''" }
    ' "$1" > "$2"
}

apply_instance_reference() {
    variable=$2
    if [ -n "$3" ]; then referenceSet=1; else referenceSet=0; fi
    if [ -z "$TRAKHOUND_CONFIGURATION_PATH" ]; then return 0; fi

    if [ -f "$instanceConfigurationFile" ]; then
        set_instance_reference "$instanceConfigurationFile" "$instanceConfigurationFile.tmp" "$1" "$variable" "$referenceSet"
    elif [ "$referenceSet" = "1" ]; then
        # The Instance writes the rest of its default settings when they're first saved
        mkdir -p "$profileDirectory"
        set_instance_reference /dev/null "$instanceConfigurationFile.tmp" "$1" "$variable" "$referenceSet"
    else
        return 0
    fi

    if cmp -s "$instanceConfigurationFile" "$instanceConfigurationFile.tmp"; then
        rm -f "$instanceConfigurationFile.tmp"
    else
        mv -f "$instanceConfigurationFile.tmp" "$instanceConfigurationFile"
        if [ "$referenceSet" = "1" ]; then
            echo "Instance setting '$1' set from $variable"
        else
            echo "Instance setting '$1' removed ($variable isn't set), so its default is used"
        fi
    fi
}

apply_instance_reference name TRAKHOUND_INSTANCE_NAME "$TRAKHOUND_INSTANCE_NAME"
apply_instance_reference description TRAKHOUND_INSTANCE_DESCRIPTION "$TRAKHOUND_INSTANCE_DESCRIPTION"

for directory in "$TRAKHOUND_DATA_PATH" "$TRAKHOUND_CONFIGURATION_PATH" "$TRAKHOUND_PACKAGE_PATH" "$TRAKHOUND_VOLUME_PATH" "$TRAKHOUND_VARIABLE_PATH"; do
    if [ -n "$directory" ] && [ -d "$directory" ] && [ ! -w "$directory" ]; then
        echo "WARNING : $directory is not writable by $(id -un) (uid $(id -u)). Change the owner of the mounted directory on the host (ex. 'chown -R $(id -u):$(id -g) [directory]')" >&2
    fi
done

exec dotnet /opt/trakhound/instance/TrakHound.Instance.dll "$@"
