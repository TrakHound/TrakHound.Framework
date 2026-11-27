# Docker Compose Examples

Docker Compose files for running a TrakHound Instance from the `trakhound/instance` and `trakhound/instance-dev` images. Copy one to a folder of its own, as `docker-compose.yml`, and edit it there. The TrakHound Instance Installation Guide (Docker section) describes the image and its variables.

| File | Image | For |
| --- | --- | --- |
| [docker-compose.yml](docker-compose.yml) | `trakhound/instance` | Running an Instance (ex. in production) : the admin account from a secret, a stable Device ID for Licenses |
| [docker-compose.dev.yml](docker-compose.dev.yml) | `trakhound/instance-dev` | Developing packages : the Dev Package Server, on this machine only, with a name and port you choose |

Each folder runs an Instance with the server a Service package uses, and that Service already configured. Install the package from Packages, and it works:

| Folder | Server | Package |
| --- | --- | --- |
| [mqtt](mqtt) | Mosquitto (MQTT Broker) | `TrakHound.Mqtt.Service` (Messages) |
| [https](https) | Caddy (reverse proxy) | none : serves the Instance over HTTPS, with Caddy as a Trusted Proxy |
| [postgres](postgres) | TimescaleDB (PostgreSQL) | `TrakHound.Postgres.Service` (Tables) and `TrakHound.Timescale.Service` (Time Series) |
| [sqlserver](sqlserver) | SQL Server 2022 (amd64 only) | `TrakHound.SqlServer.Service` (Tables) |
| [redis](redis) | Redis, shared by two Instances | `TrakHound.Redis.Service` (Key Values) |

## Run one

From the folder with the file:

```powershell
docker compose up -d
docker compose logs instance
```

Then open `http://localhost:8472`. The first start downloads the image and creates the container and its volume.

- `docker-compose.yml` creates the admin account from `trakhound_admin_password.txt` next to it (a file with the password on its first line, readable by the container's user, uid 1654). Create it before the first start, and keep it out of source control.
- `docker-compose.dev.yml` has no admin settings, so the Instance creates `admin` with a generated password on the first start, shown once in `docker compose logs instance`.

A file with another name needs `-f` on each command (ex. `docker compose -f docker-compose.dev.yml up -d`).

| Command | What it does |
| --- | --- |
| `docker compose ps` | Shows whether it's running |
| `docker compose logs -f instance` | Follows the Instance's output |
| `docker compose stop` / `docker compose start` | Stops and starts it, keeping everything |
| `docker compose pull`, then `docker compose up -d` | Updates to the newest image. The data stays in the volume |
| `docker compose down` | Removes the container. The data stays in the volume |
| `docker compose down -v` | Removes the container and its volume, to start over |

After editing the file, run `docker compose up -d` again to recreate the container with the changes.

## Variables

`docker-compose.dev.yml` reads these, so a script can start several Instances side by side, each with its own container, volume and port:

| Variable | Default | Sets |
| --- | --- | --- |
| `INSTANCE_NAME` | `trakhound-dev` | The Compose project, the container, the Instance's Name (`TRAKHOUND_INSTANCE_NAME`) and its Device ID |
| `INSTANCE_PORT` | `8472` | The port on `127.0.0.1` |
| `INSTANCE_DESCRIPTION` | `Package development` | The Instance's Description (`TRAKHOUND_INSTANCE_DESCRIPTION`) |

Set them in the terminal before each command, or in a `.env` file next to the Compose file, which Compose reads on every command:

```
INSTANCE_NAME=line1
INSTANCE_PORT=8481
INSTANCE_DESCRIPTION=Line 1 test Instance
```

```powershell
$env:INSTANCE_NAME = 'line2'; $env:INSTANCE_PORT = '8482'; docker compose -f docker-compose.dev.yml up -d
```

Use the same values with `logs`, `down` and the other commands, so they act on that Instance. One folder per Instance, each with its own `.env`, keeps them apart without setting anything.

## Publish packages to the development Instance

Push a package file, then install it from Packages > **Browse** (the `Development` Package Source):

```powershell
th packages push ./out/Contoso.Hello-0.0.1-dev.1.thp -s http://localhost:8472 --organization Dev
```

A project created from the TrakHound templates builds and publishes there with its `local` Publish Target:

```powershell
th packages dotnet --publish --target local
```

For an Instance on another port, add `--server http://localhost:<port>` to either command.
