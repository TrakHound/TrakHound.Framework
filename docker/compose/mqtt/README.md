# MQTT (Mosquitto)

A TrakHound Instance and a Mosquitto MQTT Broker, with an MQTT Message Service (`TrakHound.Mqtt.Service`) already configured for the Broker. Use it to try MQTT Messages on an Instance, or as a starting point for an Instance that talks to devices over MQTT.

| File | What it is |
| --- | --- |
| [docker-compose.yml](docker-compose.yml) | The Instance, and the `configuration` step that adds `config/` to it |
| [mosquitto.yml](mosquitto.yml) | The Broker. Included by `docker-compose.yml`, and by the other examples that use MQTT |
| [mosquitto/mosquitto.conf](mosquitto/mosquitto.conf) | The Broker's settings: one listener on port 1883 that accepts clients without a password |
| [config/service/mqtt.yaml](config/service/mqtt.yaml) | The MQTT Service's configuration. The Broker's address comes from the container's environment (`${env:MQTT_HOSTNAME}`) |
| [.env.example](.env.example) | The values you can change. Copy it to `.env` |

Needs Docker Compose 2.20 or later (for `include`). The images run on amd64 and arm64.

## Run it

```powershell
docker compose up -d
docker compose logs instance
```

1. Sign in at `http://localhost:8472/_admin` as `admin`, with the password the Instance generated on its first start (shown once in the logs above).
2. Install `TrakHound.Mqtt.Service` from Packages > **Browse**. Until then, Config > **Services** lists the MQTT configuration as *Package missing*. It starts working once the package is installed, without a restart.
3. Open Explorer > Services > **Messages** > **MQTT**, type `test/#` in the topic box and press Enter to subscribe.
4. Publish a Message to the Broker:

   ```powershell
   docker compose exec mosquitto mosquitto_pub -t test/hello -m "hello"
   ```

   It shows up in the Explorer. Publish from the Explorer's **Publish** panel, and it reaches MQTT clients the same way:

   ```powershell
   docker compose exec mosquitto mosquitto_sub -t "test/#" -v
   ```

The Broker is also published on `127.0.0.1:1883`, so MQTT clients on this machine (ex. MQTT Explorer) can connect to it. The Instance reaches it on the Compose network as `mosquitto:1883`.

`docker compose down` removes the containers and keeps the data. `docker compose down -v` also removes the Instance's and the Broker's volumes, to start over.

## How the configuration gets there

The `configuration` service runs before the Instance, copies each file in `config/` to the same place in the Instance's Configuration Profile (ex. `config/service/mqtt.yaml` to `service/mqtt.yaml`), and exits. It only adds files that aren't there yet, so:

- A change made in the Admin interface (Config > Services > **Edit**) stays, and isn't replaced when the containers start again.
- A change to the files in `config/` only applies to a new volume. Run `docker compose down -v` and `docker compose up -d` to start over with them.

Add other configurations the same way, in the folder of their category (ex. `config/api/`, `config/worker/`). The files aren't mounted into the Instance's volume directly, since Docker would create those folders for the root user, and the Instance (user `app`, uid 1654) couldn't change its own Configuration Profile.

## Use another Broker

The MQTT Service reads its connection from the Instance container's environment, so change these in `docker-compose.yml` instead of the configuration file:

| Variable | Used for |
| --- | --- |
| `MQTT_HOSTNAME` | The Broker's hostname (`mosquitto`, the Compose service) |
| `MQTT_BROKER_PORT` | The Broker's port (`1883`) |
| `MQTT_USERNAME`, `MQTT_PASSWORD` | Credentials, for a Broker that requires them. Set them in `.env` |

To require a password on this Broker, set `allow_anonymous false` in `mosquitto/mosquitto.conf` and add a `password_file` (see the [Mosquitto documentation](https://mosquitto.org/man/mosquitto-conf-5.html)). The other parameters of the MQTT Service (ex. `useTls`, `cleanSession`) are described in its [README](../../../modules/TrakHound.Mqtt.Service/README.md).

## Use it from a package

The Explorer calls the Service directly. A package (Api, App, Function or Worker) reaches Services through a Router, so add one under Config > **Routers** with a Route to the `mqtt` Service for Messages. See [Routing](https://www.trakhound.com/docs/trakhound-framework/routing).

## Variables

Set these in `.env` (copied from `.env.example`), or in the terminal before each command:

| Variable | Default | Sets |
| --- | --- | --- |
| `INSTANCE_NAME` | `trakhound-mqtt` | The Compose project, the container, the Instance's Name and its Device ID |
| `INSTANCE_PORT` | `8472` | The Instance's port on `127.0.0.1` |
| `INSTANCE_DESCRIPTION` | `MQTT example` | The Instance's Description |
| `MQTT_PORT` | `1883` | The Broker's port on `127.0.0.1` |
| `MQTT_USERNAME`, `MQTT_PASSWORD` | empty | The MQTT Service's credentials |
