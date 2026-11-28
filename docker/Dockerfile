# TrakHound Instance Docker Image
#
# Built by the release Builder ('instance docker build'), which reads this file, Dockerfile.dev and the scripts from this folder at the
# commit it builds. To build the image without the Builder, use Dockerfile.source. The Builder prepares the context in
# [projectPath]/bin/docker/[tag]:
#   instance/             The Instance published for the image's Runtime (framework-dependent)
#   default-packages/     The Default Packages (may be empty), copied to the Package directory when it has no packages yet
#                         (unless TRAKHOUND_DEFAULT_PACKAGES=false, see docker-entrypoint.sh)
#   default-configuration/ The Default Packages' Configurations (may be empty), copied with them to the Configuration Profile
#                         directory, without replacing files that are already there
#   dev-packages/         The Dev Packages (may be empty) : TrakHound.Packages.Dev.Api, only installed and configured when the
#                         container is started with TRAKHOUND_DEV_PACKAGES (see docker-entrypoint.sh)
#   docker-entrypoint.sh
#   docker-healthcheck.sh
#
# Program files: /opt/trakhound/instance (owned by root and read-only to the Instance)
# Data:          /var/lib/trakhound/instance (configuration, packages, volumes, variables, logs, backups, temporary files and
#                Startup Deployments: everything the Instance writes)
#
# Licenses are issued for the Device ID, which comes from the container's hostname unless TRAKHOUND_DEVICE_ID is set, so set
# TRAKHOUND_DEVICE_ID (or a fixed hostname) or the Licenses stop applying when the container is recreated
#
# The Base Image must match the Runtime it was published for (ex. an Alpine image needs a linux-musl-* Runtime)

ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
FROM ${BASE_IMAGE}

ARG VERSION
ARG IMAGE
LABEL org.opencontainers.image.title="TrakHound Instance" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.vendor="TrakHound Inc." \
      org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.source="https://github.com/TrakHound/TrakHound.Framework"

# The Image this container was created from ([imageName]:[version]), shown on the Instance Information page
ENV TRAKHOUND_DOCKER_IMAGE=${IMAGE}

# The Instance doesn't apply its own HTTP Address and Port in a container (DOTNET_RUNNING_IN_CONTAINER), so set the
# ASP.NET Core Port to the Instance's default Port (the Base Image sets 8080)
ENV ASPNETCORE_HTTP_PORTS=8472 \
    TRAKHOUND_CONFIGURATION_PATH=/var/lib/trakhound/instance/config \
    TRAKHOUND_PACKAGE_PATH=/var/lib/trakhound/instance/packages \
    TRAKHOUND_VOLUME_PATH=/var/lib/trakhound/instance/volumes \
    TRAKHOUND_DATA_PATH=/var/lib/trakhound/instance

USER root

# The program files stay owned by root, so the Instance's user ('app', created by the Base Image) can't change them
COPY instance/ /opt/trakhound/instance/
COPY default-packages/ /opt/trakhound/default-packages/
COPY default-configuration/ /opt/trakhound/default-configuration/
COPY dev-packages/ /opt/trakhound/dev-packages/
COPY docker-entrypoint.sh docker-healthcheck.sh /opt/trakhound/

# The directories are created here so that volumes mounted on them are owned by the Instance's user
RUN mkdir -p /var/lib/trakhound/instance/config /var/lib/trakhound/instance/packages /var/lib/trakhound/instance/volumes \
    && chown -R app:app /var/lib/trakhound \
    && chmod -R a+rX,go-w /opt/trakhound \
    && chmod 755 /opt/trakhound/docker-entrypoint.sh /opt/trakhound/docker-healthcheck.sh

USER app
WORKDIR /opt/trakhound/instance

VOLUME /var/lib/trakhound/instance
EXPOSE 8472

# Startup can take a while the first time (packages are loaded and deployments installed)
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 CMD ["/opt/trakhound/docker-healthcheck.sh"]

# The Instance can take up to 60 seconds to stop, so use a longer stop timeout (ex. 'docker stop -t 90' or 'stop_grace_period: 90s')
STOPSIGNAL SIGTERM
ENTRYPOINT ["/opt/trakhound/docker-entrypoint.sh"]
