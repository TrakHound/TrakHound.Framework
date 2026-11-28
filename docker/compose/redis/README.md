# Redis (two Instances)

Two TrakHound Instances and a Redis server, with a Redis Key-Value Service (`TrakHound.Redis.Service`) configured on both. A Key-Value set on one Instance can be read on the other, and it's kept when the Instances restart. Use Redis this way when Key-Values must be shared by more than one Instance (ex. several Instances behind a load balancer), or kept when an Instance restarts.

| File | What it is |
| --- | --- |
| [docker-compose.yml](docker-compose.yml) | The two Instances, and a `configuration` step for each that adds `config/` to it |
| [redis.yml](redis.yml) | The Redis server, with append-only persistence. Included by `docker-compose.yml` |
| [config/service/redis.yaml](config/service/redis.yaml) | The Redis Service's configuration, added to both Instances |
| [.env.example](.env.example) | The values you can change. Copy it to `.env` |

Needs Docker Compose 2.20 or later (for `include`). The images run on amd64 and arm64.

## Run it

```powershell
docker compose up -d
docker compose logs instance1 instance2
```

Instance 1 is at `http://localhost:8481` and Instance 2 at `http://localhost:8482`. Each has its own volume, and its own `admin` password, generated on its first start (shown once in the logs above).

1. On each Instance, sign in at `/_admin` and install `TrakHound.Redis.Service` from Packages > **Browse**. Until then, Config > **Services** lists the Redis configuration as *Package missing*. It starts working once the package is installed, without a restart.
2. On Instance 2, open Explorer > Services > **KeyValues** > **Redis**, click **Publish**, and publish a Value (ex. Collection `plant`, Key `shift`, Value `second shift`).
3. On Instance 1, open Explorer > Services > **KeyValues** > **Redis** > **plant**. The Key is there, with the Value set on Instance 2.
4. Restart everything (`docker compose restart`), and it's still there.

Both Instances are on `localhost`, so a browser shares their sign-in cookies: signing in to one can sign you out of the other. Use a private window for the second one, or sign in again when you switch.

Redis keeps each Key-Value as `collection:key`, so it can also be read with `redis-cli`:

```powershell
docker compose exec redis redis-cli get plant:shift
```

Redis is also published on `127.0.0.1:6379`, so tools on this machine (ex. Redis Insight) can connect. The Instances reach it on the Compose network as `redis:6379`.

`docker compose down` removes the containers and keeps the data. `docker compose down -v` also removes both Instances' volumes and Redis's, to start over.

## A password for Redis

The example's Redis has no password, which is only safe while nothing but this machine and the Instances can reach it. To require one, set `REDIS_PASSWORD` in `.env`, and start Redis with it by changing its `command` in `redis.yml`:

```yaml
    command: ["sh", "-c", "exec redis-server --appendonly yes --requirepass \"$$REDIS_PASSWORD\""]
    environment:
      REDIS_PASSWORD: ${REDIS_PASSWORD:?Set REDIS_PASSWORD in .env}
```

and the healthcheck to `["CMD-SHELL", "redis-cli -a \"$$REDIS_PASSWORD\" --no-auth-warning ping"]`. The Instances already pass `REDIS_PASSWORD` to the Service. Use `user` in `config/service/redis.yaml` for a Redis ACL user other than `default`, and `useSsl` for a server that requires TLS (see the [Redis Service README](../../../modules/TrakHound.Redis.Service/README.md)).

## More Instances

Copy `instance2` and `configuration2` in `docker-compose.yml` to `instance3` and `configuration3`, with their own volume (`instance3-data`), port and name. They all share Redis the same way.

## How the configuration gets there

Each `configuration` service runs before its Instance, copies each file in `config/` to the same place in that Instance's Configuration Profile, and exits. It only adds files that aren't there yet, so a change made in the Admin interface stays. A change to the files in `config/` only applies to a new volume: run `docker compose down -v` and `docker compose up -d` to start over with them. See the [MQTT example](../mqtt/README.md#how-the-configuration-gets-there) for why the files aren't mounted directly.

## Use it from a package

The Explorer calls the Service directly. A package (Api, App, Function or Worker) reaches Services through a Router, so add one under Config > **Routers** with a Route to the `redis` Service for Key Values, on each Instance. See [Routing](https://www.trakhound.com/docs/trakhound-framework/routing).

## Variables

Set these in `.env` (copied from `.env.example`), or in the terminal before each command:

| Variable | Default | Sets |
| --- | --- | --- |
| `INSTANCE_NAME` | `trakhound-redis` | The Compose project, and the Instances' names (`-1` and `-2`), containers and Device IDs |
| `INSTANCE1_PORT`, `INSTANCE2_PORT` | `8481`, `8482` | The Instances' ports on `127.0.0.1` |
| `REDIS_PORT` | `6379` | Redis's port on `127.0.0.1` |
| `REDIS_PASSWORD` | empty | The Redis Service's password, for a Redis server that requires one (see above) |
