# Plant

A small plant, from machine to stored data: simulated machines publish their readings to an MQTT Broker, and a Worker on the TrakHound Instance writes them to TimescaleDB, with the Instance served over HTTPS.

```
simulator ──MQTT──> Mosquitto ──> Instance: MQTT Service ──> Worker ──> TimescaleDB Service ──> TimescaleDB
                                     (served at https://localhost by Caddy)
```

| Container | What it is | From |
| --- | --- | --- |
| `simulator` | Two lines with two machines each, publishing `temperature`, `spindle_speed` and `running` every second | [simulator/simulate.sh](simulator/simulate.sh) |
| `mosquitto` | The MQTT Broker | [../mqtt](../mqtt) |
| `postgres` | TimescaleDB | [../postgres](../postgres) |
| `caddy` | The HTTPS reverse proxy | [../https](../https) |
| `instance` | The TrakHound Instance | `docker-compose.yml` |
| `configuration` | Adds the configurations below to the Instance before it starts, then exits | `docker-compose.yml` |

The servers are included from the other examples (`include` in `docker-compose.yml`), so they're the same as there. Needs Docker Compose 2.20 or later. The images run on amd64 and arm64.

The Instance gets these configurations:

| Configuration | What it does | From |
| --- | --- | --- |
| `service/mqtt.yaml` | The MQTT Service, connected to Mosquitto | [../mqtt/config](../mqtt/config) |
| `service/timescale.yaml` | The TimescaleDB Service, connected to TimescaleDB | [../postgres/config](../postgres/config) |
| `instance.config.yaml` | Caddy as a Trusted Proxy | [../https/config](../https/config) |
| `router/default.yaml` | The Router packages use: Messages to `mqtt`, Time Series to `timescale` | [config/router](config/router) |
| `worker/mqtt-to-timeseries.yaml` | The Worker that writes the readings to TimescaleDB | [config/worker](config/worker) |

## Run it

Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` to a password of your own (it's required, and every `docker compose` command reads it from `.env`).

```powershell
copy .env.example .env
docker compose up -d
docker compose logs instance
```

1. Open `https://localhost/_admin` and sign in as `admin`, with the password the Instance generated on its first start (shown once in the logs above). Your browser warns about the certificate until you trust Caddy's local Certificate Authority: see the [HTTPS example](../https/README.md#run-it).
2. Install these from Packages > **Browse**:
   - `TrakHound.Mqtt.Service`
   - `TrakHound.Timescale.Service`
   - `TrakHound.Example.MqttToTimeSeries` (the Worker, from [examples/TrakHound.Example.MqttToTimeSeries](../../../examples/TrakHound.Example.MqttToTimeSeries))

   Until then, Config lists their configurations as *Package missing*. They start without a restart, and the Worker starts writing within a few seconds.
3. Open Explorer > Services > **TimeSeries** > **TimescaleDB** > **machine**. It has a Series for each machine (`line` and `machine` Tags), with the `temperature`, `spindle_speed` and `running` Fields. Pick a Series and Fields to chart them.

The readings arrive in the Broker whether or not the Instance is running: watch them with

```powershell
docker compose exec mosquitto mosquitto_sub -t "plant/#" -v
```

The Worker logs when it subscribes and when it can't write (Explorer > the Worker, or `docker compose logs instance`). While TimescaleDB can't be reached, it keeps up to 10,000 Points and writes them once it can, so a short outage doesn't leave a gap.

`docker compose down` removes the containers and keeps the data. `docker compose down -v` also removes the volumes, to start over.

## Use real machines

Remove the `simulator` service, and have the machines (or a gateway, ex. Node-RED or an OPC UA to MQTT bridge) publish to the Broker with the same Topics: `plant/<line>/<machine>/<field>`, with a number, `true`/`false` or `{"value": ...}`. For other Topics or payloads, change the Worker's `topicPattern` and `valuePath` in Config > **Workers** (see the [Worker's README](../../../examples/TrakHound.Example.MqttToTimeSeries/README.md)).

For a Broker that other machines can reach, require a password: see the [MQTT example](../mqtt/README.md#use-another-broker). For a real domain name and certificate, set `SITE_ADDRESS`: see the [HTTPS example](../https/README.md#use-a-domain-name).

## Notes

- Caddy has a fixed address on a fixed subnet (`172.30.84.0/24`), the same as in the HTTPS example, so only one of the two can run at a time. See the [HTTPS example](../https/README.md#trusted-proxies) to change it.
- Mosquitto and TimescaleDB are also published on `127.0.0.1` (`MQTT_PORT`, `POSTGRES_PORT`), for tools on this machine.
- The configurations are copied only when they're not there yet, so a change made in the Admin interface stays. See the [MQTT example](../mqtt/README.md#how-the-configuration-gets-there).

## Variables

Set these in `.env` (copied from `.env.example`):

| Variable | Default | Sets |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | required | The database login's password, set when the database is created |
| `INSTANCE_NAME` | `trakhound-plant` | The Compose project, the container, the Instance's Name and its Device ID |
| `INSTANCE_DESCRIPTION` | `Plant example` | The Instance's Description |
| `SITE_ADDRESS` | `localhost` | The address Caddy serves, and gets a certificate for |
| `HTTP_PORT`, `HTTPS_PORT` | `80`, `443` | Caddy's ports on this machine |
| `SIMULATOR_INTERVAL` | `1` | Seconds between the simulated machines' readings |
| `MQTT_PORT`, `POSTGRES_PORT` | `1883`, `5432` | Mosquitto's and TimescaleDB's ports on `127.0.0.1` |
