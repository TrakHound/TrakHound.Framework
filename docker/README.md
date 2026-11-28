<img alt="TrakHound" src="https://static.trakhound.com/brand/icon/trakhound-icon-background-128.png" width="96">

# TrakHound Instance

A TrakHound Instance hosts TrakHound packages (Apis, Apps, Functions, Workers, Services and Identity providers), routes requests to them, and serves an Admin interface for configuring and monitoring them. It's built for manufacturing and industrial data: collecting it from machines, storing it, and serving the Apis and Apps built on it. See the [TrakHound Framework](https://github.com/TrakHound/TrakHound.Framework) for the documentation.

| Image | For |
| --- | --- |
| `trakhound/instance` | Running an Instance (ex. in production) |
| `trakhound/instance-dev` | Developing packages: the same image with the Dev Package Server turned on. Not for production |

## Quick Start

```bash
docker run -d --name trakhound -p 127.0.0.1:8472:8472 -v trakhound-data:/var/lib/trakhound/instance trakhound/instance
```

On its first start, the container creates an admin account named `admin` with a generated password, shown once in its output:

```bash
docker logs trakhound
```

Open `http://localhost:8472/_admin` and sign in. Install packages from **Packages > Browse**, and add their configurations in **Config**.

To reach the Instance from other machines, publish the port on every address (`-p 8472:8472`), and secure it first: see Security in the [User Guide](../docs/instance-user-guide.md#27-security). To serve it over HTTPS, see the [HTTPS Compose example](compose/https/README.md).

## Tags

| Tag | What it is |
| --- | --- |
| `latest` | The newest release |
| `1.1.0` | The newest release of a version (ex. `1.1.0`) |
| `1.1.0.42` | One build of a release |
| `prerelease` | The newest prerelease |
| `dev` | The newest development build |

Each tag is published for `linux/amd64` and `linux/arm64`. The image is based on `mcr.microsoft.com/dotnet/aspnet:10.0`.

## Data

Everything the Instance writes (configuration, packages, volumes, variables, logs and backups) is kept in `/var/lib/trakhound/instance`. Mount a volume there, as above, so it's kept when the container is recreated (ex. to update the image). The program files in `/opt/trakhound/instance` are read-only to the Instance.

The Instance runs as the image's `app` user, listens on port **8472** (`ASPNETCORE_HTTP_PORTS`), and has a health check that requests `/_health/live`. It can take up to 60 seconds to stop, so give it a longer stop timeout (`docker stop -t 90`, or `stop_grace_period: 90s` in Compose).

## Environment Variables

| Variable | What it does |
| --- | --- |
| `TRAKHOUND_ADMIN_USERNAME` | The first admin account's username, created when no account exists |
| `TRAKHOUND_ADMIN_PASSWORD_FILE` | A file whose first line is that account's password (ex. a Docker secret, `/run/secrets/trakhound_admin_password`) |
| `TRAKHOUND_ADMIN_GENERATE` | `false` turns off the generated `admin` account. Setting either variable above also turns it off |
| `TRAKHOUND_DEVICE_ID` | A stable Device ID (ex. the deployment's name). Set it before requesting Licenses, which are issued for the Device ID: otherwise it comes from the container's hostname, which changes when the container is recreated |
| `TRAKHOUND_INSTANCE_NAME` | The Instance's Name, read each time it starts (ex. the container's name) |
| `TRAKHOUND_INSTANCE_DESCRIPTION` | The Instance's Description, read each time it starts |
| `TRAKHOUND_DEFAULT_PACKAGES` | `false` starts a new volume without the default packages (the Welcome App at `/` and the API Explorer) |
| `TRAKHOUND_DEPLOYMENT_PATH` | A folder or a single `.thd` file of Deployments to install when the Instance starts (ex. a read-only mount) |
| `TRAKHOUND_CONFIGURATION_PROFILE` | The configuration profile to run (`default` if it isn't set) |
| `TRAKHOUND_DEV_PACKAGES` | `true` serves the Dev Package Server at `/packages`, for pushing packages being developed. Set in `trakhound/instance-dev` |
| `TRAKHOUND_DEV_UPLOAD_TOKEN` | Requires this token (at least 16 characters) for uploads to the Dev Package Server, and turns it on |

To set the admin password again (ex. if the generated one is lost):

```bash
docker exec -it trakhound /opt/trakhound/docker-entrypoint.sh set-admin admin
```

See the [Installation Guide](../docs/instance-installation-guide.md#62-containers-and-scripted-installs) (section 6.2) for each variable.

## Developing Packages

`trakhound/instance-dev` serves a Dev Package Server, so packages you build can be pushed to the container and installed from **Packages > Browse**:

```bash
docker run -d -p 127.0.0.1:8472:8472 trakhound/instance-dev
th packages push ./out/Contoso.Hello-0.0.1-dev.1.thp -s http://localhost:8472 --organization Dev
```

Anyone who can reach the port can upload packages (installing still needs an admin), so publish it on `127.0.0.1` only, or set `TRAKHOUND_DEV_UPLOAD_TOKEN`. See the [Developer Guide](../docs/developer-guide.md#25-install-it-on-an-instance) (section 2.5).

## Docker Compose

[compose](compose/README.md) has Docker Compose files for running an Instance (with the admin account from a secret) and for developing packages, and for running one with the servers its Services use: HTTPS (Caddy), MQTT (Mosquitto), PostgreSQL and TimescaleDB, SQL Server, Redis, and all of them together in a simulated plant.

## Build from Source

[Dockerfile.source](Dockerfile.source) builds the image from the repository, with the same entrypoint and health check as the released image but without its default packages. From the repository root:

```bash
docker build -f docker/Dockerfile.source -t trakhound-instance .
```

## Files

| File | What it is |
| --- | --- |
| [Dockerfile](Dockerfile) | The released `trakhound/instance` image, built by the release tooling from a published Instance |
| [Dockerfile.dev](Dockerfile.dev) | The released `trakhound/instance-dev` image: `trakhound/instance` with `TRAKHOUND_DEV_PACKAGES` set |
| [Dockerfile.source](Dockerfile.source) | Builds the image from source |
| [docker-entrypoint.sh](docker-entrypoint.sh) | Installs the default packages on a new volume, sets up the Dev Package Server and the Instance's Name, then runs the Instance |
| [docker-healthcheck.sh](docker-healthcheck.sh) | The health check |
| [compose](compose/README.md) | Docker Compose examples |

## License

The TrakHound Framework is licensed under the [Apache License, Version 2.0](../LICENSE).
