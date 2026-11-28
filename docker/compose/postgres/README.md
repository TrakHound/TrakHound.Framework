# PostgreSQL and TimescaleDB

A TrakHound Instance and a TimescaleDB server, with two Services already configured for it:

| Service | Package | Service Type |
| --- | --- | --- |
| `postgres` | `TrakHound.Postgres.Service` | Tables |
| `timescale` | `TrakHound.Timescale.Service` | Time Series |

TimescaleDB is PostgreSQL with the `timescaledb` extension, so one server backs both. Install either package, or both.

| File | What it is |
| --- | --- |
| [docker-compose.yml](docker-compose.yml) | The Instance, and the `configuration` step that adds `config/` to it |
| [postgres.yml](postgres.yml) | The database server. Included by `docker-compose.yml`, and by the other examples that use PostgreSQL or TimescaleDB |
| [config/service/postgres.yaml](config/service/postgres.yaml) | The Table Service's configuration |
| [config/service/timescale.yaml](config/service/timescale.yaml) | The Time Series Service's configuration |
| [.env.example](.env.example) | The values you can change, and the database password you must set. Copy it to `.env` |

Needs Docker Compose 2.20 or later (for `include`). The images run on amd64 and arm64.

## Run it

Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` to a password of your own. It's required, so the database never starts with a known password, and every `docker compose` command reads it from `.env`.

```powershell
copy .env.example .env
docker compose up -d
docker compose logs instance
```

1. Sign in at `http://localhost:8472/_admin` as `admin`, with the password the Instance generated on its first start (shown once in the logs above).
2. Install `TrakHound.Postgres.Service` and/or `TrakHound.Timescale.Service` from Packages > **Browse**. Until then, Config > **Services** lists their configurations as *Package missing*. Each starts working once its package is installed, without a restart.
3. **Tables**: open Explorer > Services > **Tables** > **PostgreSQL** and run a query (Ctrl+Enter):

   ```sql
   CREATE TABLE machines (id serial PRIMARY KEY, name text, temperature real);
   INSERT INTO machines (name, temperature) VALUES ('press-1', 71.5), ('lathe-2', 64.0);
   ```

   Then `SELECT * FROM machines` shows the two rows.
4. **Time Series**: open Explorer > Services > **TimeSeries** > **TimescaleDB**, click **Write**, and write a Point (ex. Measurement `machine`, Tags `machine=press-1`, Fields `temperature=71.5`). The Measurement is listed, with a chart of its Points.

The first Time Series request creates the `timescaledb` extension and the `trakhound_timeseries` Schema, which the example's login (the database's owner) can do.

The database is also published on `127.0.0.1:5432`, so database tools on this machine (ex. psql, pgAdmin) can connect with the login in `.env`. The Instance reaches it on the Compose network as `postgres:5432`.

`docker compose down` removes the containers and keeps the data. `docker compose down -v` also removes the Instance's and the database's volumes, to start over.

## PostgreSQL alone

Set `POSTGRES_IMAGE=postgres:17` in `.env` for PostgreSQL without TimescaleDB, and only install `TrakHound.Postgres.Service`. The `timescale` configuration then reports that the `timescaledb` extension isn't available: remove it in Config > **Services**, or delete `config/service/timescale.yaml` before the first start.

The image only applies to a new volume: the data in `postgres-data` belongs to the version that created it.

## Use another server

The Services read their connection from the Instance container's environment, so change these in `docker-compose.yml` instead of the configuration files:

| Variable | Used for |
| --- | --- |
| `POSTGRES_SERVER` | The server's hostname (`postgres`, the Compose service) |
| `POSTGRES_SERVER_PORT` | The server's port (`5432`) |
| `POSTGRES_DATABASE`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | The database and login. Set them in `.env` |

Their other parameters (ex. the Time Series `schema`, `chunkDays` and `compressAfterDays`) are described in the [PostgreSQL](../../../modules/TrakHound.Postgres.Service/README.md) and [TimescaleDB](../../../modules/TrakHound.Timescale.Service/README.md) Service READMEs.

## How the configuration gets there

The `configuration` service runs before the Instance, copies each file in `config/` to the same place in the Instance's Configuration Profile, and exits. It only adds files that aren't there yet, so a change made in the Admin interface stays. A change to the files in `config/` only applies to a new volume: run `docker compose down -v` and `docker compose up -d` to start over with them. See the [MQTT example](../mqtt/README.md#how-the-configuration-gets-there) for why the files aren't mounted directly.

## Use it from a package

The Explorer calls the Services directly. A package (Api, App, Function or Worker) reaches Services through a Router, so add one under Config > **Routers** with Routes to the `postgres` Service for Tables and the `timescale` Service for Time Series. See [Routing](https://www.trakhound.com/docs/trakhound-framework/routing).

## Variables

Set these in `.env` (copied from `.env.example`):

| Variable | Default | Sets |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | required | The database login's password, set when the database is created |
| `POSTGRES_USER`, `POSTGRES_DATABASE` | `trakhound` | The database login and database |
| `POSTGRES_PORT` | `5432` | The database's port on `127.0.0.1` |
| `POSTGRES_IMAGE` | `timescale/timescaledb:latest-pg17` | The database image (`postgres:17` for PostgreSQL alone) |
| `INSTANCE_NAME` | `trakhound-postgres` | The Compose project, the container, the Instance's Name and its Device ID |
| `INSTANCE_PORT` | `8472` | The Instance's port on `127.0.0.1` |
| `INSTANCE_DESCRIPTION` | `PostgreSQL and TimescaleDB example` | The Instance's Description |
