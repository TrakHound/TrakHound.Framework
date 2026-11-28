# SQL Server

A TrakHound Instance and SQL Server 2022, with a SQL Server Table Service (`TrakHound.SqlServer.Service`) already configured for it.

| File | What it is |
| --- | --- |
| [docker-compose.yml](docker-compose.yml) | The Instance, and the `configuration` step that adds `config/` to it |
| [sqlserver.yml](sqlserver.yml) | SQL Server, and the `sqlserver-setup` step that creates the database. Included by `docker-compose.yml` |
| [config/service/sqlserver.yaml](config/service/sqlserver.yaml) | The Table Service's configuration |
| [.env.example](.env.example) | The values you can change, and the password you must set. Copy it to `.env` |

Needs Docker Compose 2.20 or later (for `include`).

## Before you start

- **amd64 only.** Microsoft publishes the SQL Server image for amd64 (x86-64) only, so it doesn't run on arm64 machines such as a Raspberry Pi. On an Apple silicon Mac, Docker Desktop can run it under emulation (Settings > General > Use Rosetta), slowly.
- **Memory.** SQL Server needs about 2 GB of memory for its container.
- **License.** The example runs the free Developer edition, which can't be used in production (`SQLSERVER_EDITION`). `ACCEPT_EULA` in `sqlserver.yml` accepts the [SQL Server license](https://go.microsoft.com/fwlink/?linkid=857698).
- **Password.** `SQLSERVER_PASSWORD` must be at least 8 characters, with three of: upper case, lower case, digits and symbols. SQL Server won't start with a weaker one (`docker compose logs sqlserver` says so).

## Run it

Copy `.env.example` to `.env` and set `SQLSERVER_PASSWORD`. It's required, so the server never starts with a known password, and every `docker compose` command reads it from `.env`.

```powershell
copy .env.example .env
docker compose up -d
docker compose logs instance
```

The first start downloads the SQL Server image (about 1.5 GB). `sqlserver-setup` then creates the `trakhound` database and exits, and the Instance starts.

1. Sign in at `http://localhost:8472/_admin` as `admin`, with the password the Instance generated on its first start (shown once in the logs above).
2. Install `TrakHound.SqlServer.Service` from Packages > **Browse**. Until then, Config > **Services** lists the configuration as *Package missing*. It starts working once the package is installed, without a restart.
3. Open Explorer > Services > **Tables** > **SQL Server** and run a query (Ctrl+Enter):

   ```sql
   CREATE TABLE machines (id int IDENTITY PRIMARY KEY, name nvarchar(50), temperature real);
   INSERT INTO machines (name, temperature) VALUES ('press-1', 71.5), ('lathe-2', 64.0);
   ```

   Then `SELECT * FROM machines` shows the two rows, and the `dbo.machines` table is listed.

SQL Server is also published on `127.0.0.1:1433`, so tools on this machine (ex. SQL Server Management Studio, `sqlcmd`) can connect as `sa`, with the password in `.env`. The Instance reaches it on the Compose network as `sqlserver,1433`.

`docker compose down` removes the containers and keeps the data. `docker compose down -v` also removes the Instance's and SQL Server's volumes, to start over.

## A login for the Instance

The example connects as `sa` to keep it to one password. For anything but trying it out, give the Instance a login of its own with only the rights it needs, and set `SQLSERVER_USER` and `SQLSERVER_PASSWORD` in the `instance` service's environment to it. For example, in `sqlcmd` or SQL Server Management Studio:

```sql
CREATE LOGIN trakhound WITH PASSWORD = '<a strong password>';
USE trakhound;
CREATE USER trakhound FOR LOGIN trakhound;
ALTER ROLE db_owner ADD MEMBER trakhound;
```

## Use another server

The Service reads its connection from the Instance container's environment, so change these in `docker-compose.yml` instead of the configuration file:

| Variable | Used for |
| --- | --- |
| `SQLSERVER_SERVER` | The server's hostname (`sqlserver`, the Compose service) |
| `SQLSERVER_SERVER_PORT` | The server's port (`1433`) |
| `SQLSERVER_DATABASE` | The database |
| `SQLSERVER_USER`, `SQLSERVER_PASSWORD` | The login |

The configuration sets `trustServerCertificate`, since the container's certificate is self-signed. Set it to `false` for a server with a trusted certificate. The Service's other parameters are described in its [README](../../../modules/TrakHound.SqlServer.Service/README.md).

## How the configuration gets there

The `configuration` service runs before the Instance, copies each file in `config/` to the same place in the Instance's Configuration Profile, and exits. It only adds files that aren't there yet, so a change made in the Admin interface stays. A change to the files in `config/` only applies to a new volume: run `docker compose down -v` and `docker compose up -d` to start over with them. See the [MQTT example](../mqtt/README.md#how-the-configuration-gets-there) for why the files aren't mounted directly.

## Use it from a package

The Explorer calls the Service directly. A package (Api, App, Function or Worker) reaches Services through a Router, so add one under Config > **Routers** with a Route to the `sqlserver` Service for Tables. See [Routing](https://www.trakhound.com/docs/trakhound-framework/routing).

## Variables

Set these in `.env` (copied from `.env.example`):

| Variable | Default | Sets |
| --- | --- | --- |
| `SQLSERVER_PASSWORD` | required | The `sa` login's password, set when the server is created |
| `SQLSERVER_DATABASE` | `trakhound` | The database `sqlserver-setup` creates, and the Service uses |
| `SQLSERVER_PORT` | `1433` | SQL Server's port on `127.0.0.1` |
| `SQLSERVER_EDITION` | `Developer` | The edition (`Developer`, `Express`, or a paid edition you have a license for) |
| `INSTANCE_NAME` | `trakhound-sqlserver` | The Compose project, the container, the Instance's Name and its Device ID |
| `INSTANCE_PORT` | `8472` | The Instance's port on `127.0.0.1` |
| `INSTANCE_DESCRIPTION` | `SQL Server example` | The Instance's Description |
