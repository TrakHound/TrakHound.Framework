# HTTPS (Caddy)

A TrakHound Instance served over HTTPS by [Caddy](https://caddyserver.com), a reverse proxy that gets and renews its own certificates. The Instance serves HTTP only, so use a reverse proxy like this one to serve it over HTTPS, or on ports 80 and 443.

| File | What it is |
| --- | --- |
| [docker-compose.yml](docker-compose.yml) | The Instance, the `configuration` step that adds `config/` to it, and the network |
| [caddy.yml](caddy.yml) | Caddy. Included by `docker-compose.yml`, and by the other examples that use HTTPS |
| [caddy/Caddyfile](caddy/Caddyfile) | Caddy's settings: serve `SITE_ADDRESS` over HTTPS, and send every request to the Instance |
| [config/instance.config.yaml](config/instance.config.yaml) | The Instance Settings: Caddy's address as a Trusted Proxy |
| [.env.example](.env.example) | The values you can change. Copy it to `.env` |

Needs Docker Compose 2.20 or later (for `include`). The images run on amd64 and arm64.

## Run it

```powershell
docker compose up -d
docker compose logs instance
```

Then open `https://localhost`, and sign in at `https://localhost/_admin` as `admin`, with the password the Instance generated on its first start (shown once in the logs above). `http://localhost` redirects to HTTPS.

For `localhost`, Caddy issues the certificate from its own local Certificate Authority, which your browser doesn't trust yet, so it shows a warning. To trust it on this machine, copy Caddy's root certificate out of the container and add it to the Trusted Root Certification Authorities (on Windows, open the file and choose **Install Certificate**):

```powershell
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt caddy-root.crt
```

The certificate authority is kept in the `caddy-data` volume, so it stays the same until `docker compose down -v`.

The Instance's own port (8472) isn't published, so every request goes through Caddy.

## Use a domain name

Set `SITE_ADDRESS` in `.env` to a domain name that points to this machine (ex. `trakhound.example.com`), with ports 80 and 443 open to the internet. Caddy then gets a certificate from Let's Encrypt and renews it by itself. See [Automatic HTTPS](https://caddyserver.com/docs/automatic-https).

On a machine where ports 80 and 443 are in use, set `HTTP_PORT` and `HTTPS_PORT` in `.env` (ex. `8080` and `8443`) and open `https://localhost:8443`. The redirect from HTTP still points to port 443, so open the HTTPS address directly. Let's Encrypt needs ports 80 and 443.

## Trusted Proxies

Behind a proxy, every request reaches the Instance from the proxy's address. Caddy adds the `X-Forwarded-For` (the client's address) and `X-Forwarded-Proto` (`https`) headers, and the Instance uses them for requests from its **Trusted Proxies** (Settings > Interfaces). The client's address is then used in logs, the **HTTP Requests** list and Rate Limits, and the links the Instance gives (ex. `/llms.txt`) use `https`.

Caddy has a fixed address (`172.30.84.10`) on a network with a fixed subnet (`172.30.84.0/24`), so `config/instance.config.yaml` can list it. If that subnet is already used on this machine, change it in `docker-compose.yml`, `caddy.yml` and `config/instance.config.yaml`. Trust only the proxy's address: a client that can reach the Instance directly could otherwise send its own `X-Forwarded-For`.

The TrakHound Instance Installation Guide (section 5.3) covers reverse proxies in more detail, including nginx.

## How the configuration gets there

The `configuration` service runs before the Instance, copies each file in `config/` to the same place in the Instance's Configuration Profile (here `instance.config.yaml`), and exits. It only adds files that aren't there yet, so a change made in the Admin interface stays. A change to the files in `config/` only applies to a new volume: run `docker compose down -v` and `docker compose up -d` to start over with them. See the [MQTT example](../mqtt/README.md#how-the-configuration-gets-there) for why the files aren't mounted directly.

## Variables

Set these in `.env` (copied from `.env.example`), or in the terminal before each command:

| Variable | Default | Sets |
| --- | --- | --- |
| `INSTANCE_NAME` | `trakhound-https` | The Compose project, the container, the Instance's Name and its Device ID |
| `INSTANCE_DESCRIPTION` | `HTTPS example` | The Instance's Description |
| `SITE_ADDRESS` | `localhost` | The address Caddy serves, and gets a certificate for |
| `HTTP_PORT`, `HTTPS_PORT` | `80`, `443` | Caddy's ports on this machine |
