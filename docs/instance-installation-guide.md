![TrakHound](images/trakhound-guide-header.png)

# TrakHound Instance Installation Guide

Oct 18, 2026 · Patrick Ritchie

## Contents

1. Overview
2. Requirements
3. Install as a Windows Service
    - 3.1 What Setup creates
4. Install in IIS
    - 4.1 Create the Application Pool
    - 4.2 Create the site
    - 4.3 Run the installer
    - 4.4 What Setup changes
5. Install on Linux
    - 5.1 Install with install.sh
    - 5.2 Build the Linux release from source
    - 5.3 Serve HTTPS with a reverse proxy
    - 5.4 Upgrading and uninstalling on Linux
6. First sign-in
    - 6.1 The set-admin command
    - 6.2 Containers and scripted installs
    - 6.3 Connecting to a TrakHound Package Manager
    - 6.4 The device-id command
7. Upgrading and converting
    - 7.1 Converting between hosting types
8. Silent installs
    - 8.1 Installing with a deployment
9. Uninstalling
10. Troubleshooting installation

## 1. Overview

The TrakHound Instance is a server application that runs your TrakHound packages (Apps, APIs, Workers, Services and Functions). This guide covers installing, upgrading, converting and uninstalling it. For using the Admin interface and configuring a running Instance, see the TrakHound Instance User Guide (`instance-user-guide.pdf`).

On Windows, one installer covers two hosting types. You choose between them on the installer's **Hosting** page:

| Hosting | Best for | How it runs |
| --- | --- | --- |
| Windows Service (default) | Most installs. One Instance per server, served on its own port | The service `TrakHound-Instance` runs the Instance with its built-in web server |
| IIS Application | Servers that already use IIS for TLS, shared ports 80/443 or host names | IIS runs the Instance in-process inside an IIS site you set up beforehand |

On Linux, a release archive with an install script installs the Instance as a systemd service (section 5).

## 2. Requirements

This section covers Windows. For Linux, see section 5. On Windows you need administrator rights and the .NET 10 runtime that matches your hosting type. The installer checks for the runtime and stops with a download link if it is missing.

| Requirement | Windows Service | IIS Application |
| --- | --- | --- |
| Operating system | 64-bit or 32-bit Windows. The installer picks the x64 or x86 build automatically | Windows with IIS installed |
| .NET runtime | ASP.NET Core Runtime 10.0, same architecture as Windows ([download](https://dotnet.microsoft.com/download/dotnet/10.0)) | ASP.NET Core **Hosting Bundle** 10.0 (includes the runtime and the IIS module) |
| IIS features | Not needed | **WebSocket Protocol** (required; the Admin interface and Apps use it). **Application Initialization** (recommended, so the Instance starts without waiting for a request) |
| Network | A free TCP port. Default **8472** | The IIS site's bindings (ex. 80/443) |
| Account | Administrator, to run the installer | Administrator, plus an Application Pool identity with Modify permission on the Instance folders |

Install the Hosting Bundle **after** IIS. If IIS was added later, run the Hosting Bundle installer again (Repair).

## 3. Install as a Windows Service

Run the installer (`trakhound-instance-<version>-install.exe`) as an administrator. Then go through these pages:

1. **Welcome** and **License**: read and accept the license.
2. **Hosting**: choose **Windows Service**.
3. **Install directory**: the default is `C:\Program Files\TrakHound\Instance`. This folder holds the program files only.
4. **Instance Port**: the HTTP port for the Admin interface, Apps and APIs. The default is **8472**. Setup warns you if another program already listens on that port. On an upgrade this page is skipped and the existing port is kept.
5. **Tasks**: choose what Setup should do. The defaults are below.
    - Create a Start Menu icon and a Desktop icon that open the Admin interface (on by default)
    - Start the service automatically on startup (on by default; uses Automatic, Delayed Start)
    - Start the service now (on by default)
    - Allow incoming connections to the port through Windows Firewall (off by default; turn on to reach the Instance from other computers)
6. **Default Packages** (new installs only): leave **Install the default packages and their configurations** ticked to install the packages listed on the page (ex. a Welcome page at `/` and the example packages), each with a configuration, as installing them from Packages in the Admin interface would. Untick it to start with an empty Instance. You can remove them later from Packages and Config. This page isn't shown on an upgrade, or when the Instance already has packages.
7. **Deployment** (optional): select a deployment file (`.thd`) to set up the Instance with specific configurations and packages, or leave it empty. Setup copies the file and the Instance installs it when it starts (section 8.1). On an upgrade this page starts empty, so nothing is installed unless you select a file.
8. **Ready**: check the install directory, data location, Admin address, service name, default packages and deployment file, then click **Install**.
9. **Finished**: Setup shows whether the service is running and the Admin address. Leave **Open TrakHound Admin** ticked to open it, then continue with section 6.

### 3.1 What Setup creates

| Item | Location or value |
| --- | --- |
| Program files | `C:\Program Files\TrakHound\Instance` |
| Configuration | `C:\ProgramData\TrakHound\Instance\config\default` |
| Packages | `C:\ProgramData\TrakHound\Instance\packages` |
| Volumes (package data) | `C:\ProgramData\TrakHound\Instance\volumes` |
| Variables (package values) | `C:\ProgramData\TrakHound\Instance\variables` |
| Logs | `C:\Program Files\TrakHound\Instance\logs` |
| Windows Service | `TrakHound-Instance` (display name TrakHound Instance), runs as Local System, restarts automatically if it fails |

The ProgramData folder is restricted to SYSTEM and Administrators. The service finds it through environment variables set on the service (`TRAKHOUND_CONFIGURATION_PATH`, `TRAKHOUND_PACKAGE_PATH`, `TRAKHOUND_VOLUME_PATH`). The Variables folder is kept beside the configuration folder unless `TRAKHOUND_VARIABLE_PATH` sets another one, and the installed deployment's information (`trakhound-deployment.json`) is kept in the configuration folder.

## 4. Install in IIS

Create the IIS site and Application Pool first, then run the installer into the site's physical path. The installer doesn't create sites or pools; it installs the files and sets the data paths in the site's `web.config`.

### 4.1 Create the Application Pool

The Instance is a long-running server, so the pool must never idle, recycle or overlap. In IIS Manager, add a pool for this Instance only, then set these values (Advanced Settings):

| Setting | Value | Why |
| --- | --- | --- |
| .NET CLR version | No Managed Code | ASP.NET Core doesn't use the .NET Framework CLR |
| Enable 32-Bit Applications | False (on 64-bit Windows) | Must match the installed x64 build |
| Start Mode | AlwaysRunning | Starts the pool with Windows |
| Idle Time-out (minutes) | 0 | The default 20 minutes would stop every Worker and Service when there is no web traffic |
| Regular Time Interval (recycling) | 0 | Prevents scheduled restarts |
| Disable Overlapped Recycle | True | Two copies of the Instance must never run at once |
| Shutdown Time Limit (seconds) | 90 or more | The Instance can take up to 60 seconds to stop |
| Load User Profile | True |  |

### 4.2 Create the site

1. Create a folder for the site, ex. `D:\Sites\TrakHound`.
2. Add a website that uses that folder and the new Application Pool, with the bindings you need (ex. `https` on 443 with your certificate).
3. In the site's Advanced Settings, set **Preload Enabled** to True (needs Application Initialization).
4. Give the pool identity (`IIS AppPool\<pool name>`) **Modify** permission on the site folder. The Instance writes configuration, packages, logs and temporary files there.

Host the Instance at the root of the site, not in a virtual directory or sub-application.

### 4.3 Run the installer

1. On the **Hosting** page, choose **IIS Application**.
2. On the directory page, select the site's physical path (ex. `D:\Sites\TrakHound`). Setup asks you to confirm if the folder has no `web.config`.
3. Setup skips the Port and Tasks pages; the site's bindings set the address.
4. On a new install, choose whether to install the default packages on the **Default Packages** page (section 3).
5. On the **Deployment** page, optionally select a deployment file (section 8.1).
6. Check the **Ready** page. It lists the folders the pool identity needs Modify permission on.
7. After installing, browse to the site (ex. `https://trakhound.example.com/_admin`). The Instance starts on the first request, or when the pool starts if Preload is enabled. Then continue with section 6.

### 4.4 What Setup changes

- **Files:** copied into the site folder. An existing `web.config` is kept; only its TrakHound paths change.
- **Data:** stored in `config`, `packages` and `volumes` inside the site folder, unless `web.config` already points elsewhere or an existing Windows Service's data is kept (section 7.1).
- **web.config:** Setup sets `TRAKHOUND_CONFIGURATION_PATH`, `TRAKHOUND_PACKAGE_PATH` and `TRAKHOUND_VOLUME_PATH` under `<aspNetCore><environmentVariables>`. The shipped file runs in-process, allows 5 minutes for startup and lifts IIS's 30 MB upload limit, so the Instance's own **Max Request Size** (256 MB by default, Settings > Interfaces) applies.
- **Windows Service:** if one is installed, Setup asks to stop and remove it (see section 7.1).

The separate Admin port (`adminHttpPort`) isn't available under IIS. Restrict `/_admin` with IIS IP and Domain Restrictions if the site is public.

## 5. Install on Linux

The Linux release is an archive for each architecture: `trakhound-instance-<version>-linux-x64.tar.gz` and `trakhound-instance-<version>-linux-arm64.tar.gz`. It includes the .NET runtime, so the server doesn't need .NET installed. Its `install.sh` script installs the Instance as a systemd service, and upgrades or uninstalls it. The Instance serves HTTP only, so use a reverse proxy for HTTPS (section 5.3).

You need:

- 64-bit Linux (x64 or Arm64) with systemd, and root access (`sudo`).
- ICU, from your distribution's `libicu` package (ex. `libicu74` on Ubuntu 24.04, `libicu72` on Debian 12, `libicu` on RHEL or Fedora). Most installs already have it; `install.sh` stops and says so if it's missing.
- A free TCP port. Default **8472**.
- Packages that run on Linux. A package that uses Windows-only features (ex. the Windows Registry) doesn't load.

`install.sh` uses these locations:

| Item | Location or value |
| --- | --- |
| Program files | `/opt/trakhound/instance` |
| Configuration | `/var/lib/trakhound/instance/config/default` |
| Packages | `/var/lib/trakhound/instance/packages` |
| Volumes (package data) | `/var/lib/trakhound/instance/volumes` |
| Logs | `/opt/trakhound/instance/logs`, and the systemd journal |
| Service | `trakhound-instance`, runs as the `trakhound` user, restarts automatically if it fails |

### 5.1 Install with install.sh

Download the archive for the server's architecture (`uname -m` shows `x86_64` for x64 or `aarch64` for Arm64), then extract it and run `install.sh`:

```
tar xzf trakhound-instance-<version>-linux-x64.tar.gz
sudo bash trakhound-instance/install.sh
```

`install.sh` then:

1. Checks that the archive matches the machine's architecture and that ICU is installed.
2. Creates the `trakhound` system user, which can't sign in.
3. Copies the program files to `/opt/trakhound/instance` and creates the data folder. Only the `trakhound` user can open the data folder. On Windows the keys that protect sign-in cookies are encrypted for the machine, but on Linux they're stored unencrypted in `config/default/.local/data-protection`. The Instance also limits that folder, the key for encrypted configuration values (`config/.secretkey`) and the admin account file (`.user`) to the service user when it starts. The `trakhound` user also owns the program folder, because the Instance writes its logs, temporary files and deployment backups there.
4. Asks for the admin username and password, if no account exists and you run it in a terminal. It creates the account with `set-admin` (section 6.1).
5. Installs the `trakhound-instance` systemd service, enables it so it starts with the system, and starts it. The service tells systemd when the Instance has finished starting, so `install.sh` waits until it's ready.

It ends with the Admin interface address. Then continue with section 6.

| Option | What it does |
| --- | --- |
| `--port <port>` | HTTP port. New installs only; an upgrade keeps the configured port. Default **8472** |
| `--admin-user <name>` | Creates an Owner admin account, or sets the password of the account with that username. The password is asked for, or read from the first line of standard input when input is redirected |
| `--admin-password-file <file>` | Reads the admin password from the first line of a file (with `--admin-user`) |
| `--deployment <file.thd>` | A deployment to install when the Instance starts (section 8.1) |
| `--no-start` | Installs without starting the service |
| `--uninstall` | Removes the service and the program files, and keeps the data (section 5.4) |
| `--purge` | With `--uninstall`, also deletes the data and the `trakhound` user |

Example: an unattended install on port 9000, with the admin account and a deployment:

```
sudo bash trakhound-instance/install.sh --port 9000 --admin-user admin --admin-password-file /root/trakhound-admin.txt --deployment site-a-1.0.0.thd
```

To reach the Instance from other computers, open the port in the firewall (ex. `sudo ufw allow 8472/tcp`, or `sudo firewall-cmd --permanent --add-port=8472/tcp` followed by `sudo firewall-cmd --reload`). Skip this if you use a reverse proxy.

To check on the service, run `systemctl status trakhound-instance`. To follow its output, run `journalctl -u trakhound-instance -f`.

`install.sh` replaces `/etc/systemd/system/trakhound-instance.service` on every install and upgrade. To change a service setting (ex. add an environment variable such as `TRAKHOUND_DEPLOYMENT_PATH`), run `sudo systemctl edit trakhound-instance`. It keeps your change in a separate file that upgrades don't replace.

### 5.2 Build the Linux release from source

To build the files in the archive yourself, publish the Instance from the repository folder with the .NET 10 SDK. It can run on Windows or Linux.

```
dotnet publish instances/TrakHound.Instance/TrakHound.Instance.csproj -c Release -f net10.0 -r linux-x64 --self-contained true -o publish/trakhound-instance
```

Use `-r linux-arm64` for Arm64 servers. The folder includes `install.sh` and `trakhound-instance.service`. Copy it to the server (ex. with `scp` or `rsync`) and run `sudo bash trakhound-instance/install.sh` from the folder that contains it.

### 5.3 Serve HTTPS with a reverse proxy

To serve HTTPS, or ports 80 and 443, put a reverse proxy such as nginx, Apache or Caddy in front of the Instance.

1. In the Admin interface, open Config > Settings > Interfaces and set **HTTP Address** to `127.0.0.1` (`httpAddress` in `instance.config.yaml`), then restart the Instance. The Instance then accepts connections only from the proxy on the same server.
2. In the same section, under Reverse Proxy, add `127.0.0.1` to **Trusted Proxies** (`trustedProxies` in `instance.config.yaml`) and save. See below.
3. Configure the proxy to forward the whole site to `http://127.0.0.1:8472`, with the `X-Forwarded-For` and `X-Forwarded-Proto` headers. Host the Instance at the root of the site, not under a path.
4. Allow WebSockets. The Admin interface and Apps use them, and their connections stay open for as long as a page is open.
5. Lift the proxy's upload limit. Package and deployment uploads can be large.

Example for nginx:

```
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 443 ssl;
    server_name trakhound.example.com;

    ssl_certificate     /etc/ssl/certs/trakhound.example.com.crt;
    ssl_certificate_key /etc/ssl/private/trakhound.example.com.key;

    client_max_body_size 0;

    location / {
        proxy_pass http://127.0.0.1:8472;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 1h;
    }
}
```

Behind a proxy, every request reaches the Instance from the proxy's address. **Trusted Proxies** tells the Instance which addresses are your proxies. For requests from them, it takes the client's address from `X-Forwarded-For` and the scheme (`https`) from `X-Forwarded-Proto`. The client's address is then used in logs, the **HTTP Requests** list, Rate Limits, and the check that Module Tokens come from the same server. Without it, the Instance treats every client as the proxy: anonymous Rate Limits count all clients together, and Module Tokens are accepted from any client that has one.

- List only your own proxies. Each entry is an IP Address (ex. `127.0.0.1`) or Network (ex. `10.0.0.0/8`). Any client at a listed address can set its own address.
- With more than one proxy in a row (ex. a load balancer and nginx), list all of them. The Instance reads `X-Forwarded-For` from the right and stops at the first address that isn't a Trusted Proxy, so an address a client adds to the header itself is never used.
- Changes apply when saved. Entries that aren't an address or network are ignored, with a warning on the Settings page and in the log.
- With the Admin interface limited to **Allowed Addresses** (User Guide, section 3.2), set Trusted Proxies first. Without them, the proxy's address is checked, and a proxy on the same computer counts as loopback, which is always allowed.

### 5.4 Upgrading and uninstalling on Linux

To upgrade, extract the new version's archive and run its `install.sh`, the same way as a new install. It stops the service, replaces the program files and the service file, and starts it again. The configuration, packages, volumes, port and admin account are kept.

```
tar xzf trakhound-instance-<version>-linux-x64.tar.gz
sudo bash trakhound-instance/install.sh
```

To uninstall, run `install.sh` with `--uninstall`. It stops and removes the service and the program files, and keeps the data in `/var/lib/trakhound/instance` for a later install. Add `--purge` to also delete the data and the `trakhound` user.

```
sudo bash trakhound-instance/install.sh --uninstall
```

## 6. First sign-in

The first admin account is created on the machine the Instance runs on, never in the browser, so whoever reaches a new Instance first can't claim it. There are no default credentials. The username and password must each be at least 4 characters. This account is an **Owner**: it can do everything, and it adds the other admin accounts and chooses their roles on Security > **Admin Accounts** (User Guide, section 2.7.3).

1. Create the first admin account:
    - **Windows installer** (Windows Service or IIS): enter the account on the **Admin Account** page and Setup creates it with `set-admin` (required for a new install; on an upgrade the page is skipped if an account exists). For a silent install, pass `/ADMINUSER=` with `/ADMINPASSWORDFILE=` (first line of the file) or `/ADMINPASSWORD=`.
    - **Linux**: `install.sh` asks for the account when you run it in a terminal, or takes `--admin-user` (section 5.1).
    - **Any other install**, or to get back in later when no Owner can sign in: run the `set-admin` command from the program folder (section 6.1).
    - **Containers**: set `TRAKHOUND_ADMIN_USERNAME` and `TRAKHOUND_ADMIN_PASSWORD_FILE` (section 6.2).
2. Open the Admin interface: `http://localhost:8472/_admin` for a Windows Service or Linux (use your port if you changed it), or `/_admin` on the IIS site or behind your reverse proxy. If no account exists yet, the page says how to create one.
3. Sign in with that account. Sessions last 7 days and survive restarts.

### 6.1 The set-admin command

`set-admin` creates an Owner admin account. With the username of an account that already exists, it sets that account's password, enables it and makes it an Owner (ex. to reset a forgotten password when no Owner can sign in). The other accounts aren't changed. It asks for the password twice, or reads it from the first line of standard input when input is redirected, so the password isn't kept in the command line or shell history. Run it as a user that can write to the configuration folder.

```
TrakHound.Instance set-admin <username> [--profile <id>] [--configurationPath <path>]
```

- `--configurationPath` is the configuration folder (the one that holds the profile folders). The default is `config` in the program folder, or `TRAKHOUND_CONFIGURATION_PATH` when it's set in the shell. A Windows Service and Linux keep their configuration elsewhere and set that variable only for the service, so pass the folder: `C:\ProgramData\TrakHound\Instance\config` (Windows Service) or `/var/lib/trakhound/instance/config` (Linux).
- `--profile` is the configuration profile. The default is `default`, or `TRAKHOUND_CONFIGURATION_PROFILE` when it's set.
- The command prints the folder it wrote to (`security/admins` in the configuration profile folder). It takes effect within a few seconds, even while the Instance is running, and setting the password of an existing account signs out that account's sessions.

Windows Service (from an administrator prompt):

```
"C:\Program Files\TrakHound\Instance\TrakHound.Instance.exe" set-admin admin --configurationPath "C:\ProgramData\TrakHound\Instance\config"
```

Linux (as the service user, so the file has the right owner):

```
sudo -u trakhound /opt/trakhound/instance/TrakHound.Instance set-admin admin --configurationPath /var/lib/trakhound/instance/config
```

### 6.2 Containers and scripted installs

When the Instance starts with no admin account, it creates the first one (an Owner) from these environment variables:

| Variable | Value |
| --- | --- |
| `TRAKHOUND_ADMIN_USERNAME` | The admin username |
| `TRAKHOUND_ADMIN_PASSWORD_FILE` | A file whose first line is the password (ex. a Docker secret, `/run/secrets/trakhound_admin_password`). The file is read, not changed or deleted |

They're ignored once any admin account exists, so changing them doesn't change the account; use Security > Admin Accounts or `set-admin` for that. A password in an environment variable itself isn't supported, since it's visible to anyone who can inspect the process or container. If the account can't be created (ex. the file is missing), the reason is logged and shown as a notification.

In a container, also set `TRAKHOUND_DEVICE_ID` to a stable value (ex. the deployment's name) before requesting licenses. Licenses are issued for the Device ID, which otherwise comes from the container's hostname, and that changes whenever the container is recreated (ex. to update the image). Keep the configuration, packages, volumes and variables on a volume (the Docker image keeps them all in `/var/lib/trakhound/instance`).

Next, install packages and add configurations: see the TrakHound Instance User Guide. To script them, use the TrakHound CLI: `th packages install` installs packages, `th config` adds and changes configurations and variables, `th instance set` changes the Instance settings (ex. the HTTP port), and `th config validate` checks the result (see the CLI README).

### 6.3 Connecting to a TrakHound Package Manager

If your organization runs a TrakHound Package Manager, register the Instance there to get the feed URL and an access token. Then, in Packages > Sources, **Add Source** with that URL as the Base URL and the token as the Access Token, and tick **TrakHound Package Manager**. The Instance then only sees the packages approved for it, reports its installed packages to the Package Manager (at startup, after changes, and every 6 hours unless `packageInventoryReportInterval` is changed), and trusts the publishers the Package Manager verified. To refuse any package the Package Manager hasn't approved (ex. one copied onto the Instance by hand), set `packageVerification` to `RequireApproval`, then confirm the pinned server key in Security > Trusted Publishers. See the User Guide, sections 2.4, 3.7 and 3.7.1.

The source is kept in `remotes.config.yaml` in the configuration profile folder, so a scripted install can write it there:

```yaml
remotes:
- id: package-manager
  name: Contoso Package Manager
  baseUrl: https://pm.contoso.com/trakhound/package-manager/api/feed
  organization: Contoso
  accessTokenCode: <token>
  packageManager: true
```

### 6.4 The device-id command

Licenses are issued for a Device ID. On an Instance without the Admin interface (ex. a device with no screen), `device-id` shows it, so you can request a license for that device:

```
TrakHound.Instance device-id [--json]
```

- It writes the Device ID alone to standard output, so a script can read it (ex. `id=$(./TrakHound.Instance device-id)`), and where it came from to standard error.
- `--json` writes one object instead : `deviceId`, `source` (`machineName` or `variable`), `machineName`, `container` and `warning`.
- The Device ID comes from the machine name, so renaming the device changes it and its licenses no longer apply. In a container it comes from `TRAKHOUND_DEVICE_ID` (TrakHound Instance User Guide, section 2.8), and the command says why when the variable is ignored.
- The Instance also shows the Device ID when it starts and writes it to its log.

## 7. Upgrading and converting

To upgrade, run the newer installer. Setup pre-selects the current hosting type and install directory, keeps your configuration, packages and volumes, and keeps the existing port.

| Hosting | What Setup does during an upgrade |
| --- | --- |
| Windows Service | Stops the service (waits up to 60 seconds), replaces the files, then starts it again if **Start the service now** is ticked |
| IIS Application | Adds `app_offline.htm` to the site so IIS stops the Instance (waits up to 90 seconds), replaces the files, then removes it. The Instance starts on the next request |

### 7.1 Converting between hosting types

Select the other option on the **Hosting** page. If the Instance has run with the previous hosting type, the configuration, packages and volumes stay where they are and the new hosting type is pointed at them. If it never ran (ex. a Windows Service that was installed but never started), the new hosting type uses its own default data folders: the site folder for IIS, `C:\ProgramData\TrakHound\Instance` for the Windows Service.

**Windows Service to IIS**

1. Set up the IIS site and pool as described in section 4.
2. Run the installer, choose **IIS Application** and confirm that the service will be removed.
3. Select the site's physical path.
4. If the service had run, the site's `web.config` points to the existing data in `C:\ProgramData\TrakHound\Instance`. Give the pool identity **Modify** permission on that folder; by default only SYSTEM and Administrators can open it.

**IIS to Windows Service**

1. Run the installer, choose **Windows Service** and confirm.
2. Setup takes the IIS site offline with `app_offline.htm` and leaves it offline, so IIS doesn't run a second copy of the Instance against the same data.
3. If the Instance had run under IIS, the service uses the data paths from the site's `web.config`.
4. Afterwards, remove the site and Application Pool from IIS.

If you pick a different install directory while converting, the previous directory's program files are not deleted. The **Ready** page lists the folder left behind.

## 8. Silent installs

The installer accepts command-line options for unattended installs. Run it from an administrator prompt.

| Option | What it does |
| --- | --- |
| `/VERYSILENT` | No wizard and no progress window (`/SILENT` shows progress only) |
| `/SUPPRESSMSGBOXES` | Answers message boxes with their default |
| `/HOSTING=Service` or `/HOSTING=IIS` | Hosting type. Default: the installed version's, or Service |
| `/DIR="path"` | Install directory (the IIS site's physical path for IIS) |
| `/PORT=8472` | HTTP port. New Windows Service installs only; upgrades keep the configured port |
| `/TASKS="..."` | Tasks to run, comma-separated: `startmenuicon`, `desktopicon`, `autostart`, `startservice`, `firewall`. Tasks not listed are off |
| `/LOG="path"` | Writes a Setup log to the file (Setup also logs to `%TEMP%` by default) |
| `/DEFAULTPACKAGES=no` | Don't install the default packages. New installs install them (with their configurations) unless this is set |
| `/DEPLOYMENT="path"` | A deployment file (`.thd`) to install. Setup copies it to the `deployments` folder in the install directory, and the Instance installs it when it starts (see below) |

Example: a Windows Service on port 9000, started automatically, with a firewall rule:

```
trakhound-instance-<version>-install.exe /VERYSILENT /SUPPRESSMSGBOXES /HOSTING=Service /PORT=9000 /TASKS="autostart,startservice,firewall"
```

Example: install into an existing IIS site:

```
trakhound-instance-<version>-install.exe /VERYSILENT /SUPPRESSMSGBOXES /HOSTING=IIS /DIR="D:\Sites\TrakHound"
```

### 8.1 Installing with a deployment

To set up an Instance with specific configurations and packages, create a deployment on an Instance that is already set up (Deploy > Create in the Admin interface, or `th deployments create`) and select it on the installer's **Deployment** page, or pass it to Setup with `/DEPLOYMENT`:

```
trakhound-instance-<version>-install.exe /VERYSILENT /SUPPRESSMSGBOXES /HOSTING=Service /TASKS="autostart,startservice,firewall" /DEPLOYMENT="C:\Setup\site-a-1.0.0.thd"
```

- Setup only copies the file (to `deployments` in the install directory), so the original can be moved or deleted after Setup finishes. The Instance installs it the next time it starts, before it reads its configuration, so a deployment that includes the Instance Settings can also change the port and other settings.
- The Instance backs up first. If the install fails, it restores the backup and starts without the deployment. A notification in the Admin interface shows the result either way.
- Each file is installed once, so passing the same file again on an upgrade doesn't overwrite changes made since. A new version of the deployment is installed.
- On IIS, the Instance installs the file on the site's first request after Setup. The Application Pool identity needs Modify permission on the install directory (see section 4).
- Deployments created without Include Secrets don't carry admin accounts. The first account comes from Setup, or from `set-admin` (section 6).

Create startup deployments without Include Secrets, and use Variables for values that differ per site. See "Installing a deployment at startup" in the TrakHound Instance User Guide.

## 9. Uninstalling

This section covers Windows. For Linux, see section 5.4.

Open **Settings > Apps > Installed apps**, find **TrakHound Instance** and choose **Uninstall**.

1. Setup asks whether to also delete the Instance data (configuration, packages and volumes) and lists the folders. Choose **No** to keep them for a later reinstall.
2. **Windows Service:** Setup stops and removes the service and its firewall rule, then removes the program files.
3. **IIS Application:** Setup takes the site offline with `app_offline.htm` and removes the program files. The IIS site and Application Pool stay; remove them in IIS Manager.

## 10. Troubleshooting installation

Start with the Setup log in `%TEMP%` (`Setup Log <date> #<n>.txt`), the Instance's `logs` folder next to the program files, and Windows Event Viewer (Windows Logs > Application and System). On Linux, start with `systemctl status trakhound-instance` and `journalctl -u trakhound-instance`. For problems with a running Instance, see the troubleshooting section of the TrakHound Instance User Guide.

| Problem | What to do |
| --- | --- |
| Windows says the installer "could be a virus or potentially unwanted software" and blocks it | Microsoft Defender flagged the download. In Windows Security, open Virus & threat protection > Protection history, select the TrakHound entry and choose **Actions > Allow on device**, then download it again. Report the threat name to TrakHound |
| "Windows protected your PC" (SmartScreen) | Click **More info**, then **Run anyway** |
| Setup says the ASP.NET Core Runtime or Hosting Bundle is required | Install the 10.0 version from the link Setup offers, then run Setup again. Windows Service needs the runtime for your Windows architecture; IIS needs the Hosting Bundle |
| Setup warns that the port is in use | Another program listens on that port. Choose another port, or stop that program. To see which program, run netstat -ano in a command prompt and look up the PID of the line for your port in Task Manager (Details tab) |
| The service installs but doesn't start | Read the error Setup shows, then the Instance logs and Event Viewer. Common causes: the port is in use, or `instance.config.yaml` has an error |
| Admin page doesn't open from another computer | Turn on the firewall task (or add an inbound rule for the port), and check `httpAddress` isn't `127.0.0.1` |
| IIS: HTTP Error 500.19 | IIS can't read `web.config`. Install the ASP.NET Core Hosting Bundle (Repair it if IIS was added after it) |
| IIS: HTTP Error 500.30 or 500.31 | The Instance failed to start or can't find the runtime. Check the Hosting Bundle version, Event Viewer, and that the pool identity has Modify permission on the site and data folders |
| IIS: Admin pages load but don't respond | Install the IIS **WebSocket Protocol** feature |
| IIS: Workers stop after a while | The Application Pool is idling or recycling. Set Idle Time-out and Regular Time Interval to 0, and Start Mode to AlwaysRunning (section 4.1) |
| IIS: site shows "being updated" after Setup finished | Delete `app_offline.htm` from the site folder (left there on purpose after converting to the Windows Service or uninstalling) |
| Linux: the service fails with `status=203/EXEC` | systemd can't run the program. Run `install.sh` again to restore the program files, and check `ExecStart` if you changed it with `systemctl edit` |
| Linux: `install.sh` says "ICU was not found" | Install your distribution's `libicu` package (section 5), then run `install.sh` again |
| Linux: `install.sh` says the release "isn't built for this machine" | Download the archive for the server's architecture: `linux-x64` for `x86_64`, `linux-arm64` for `aarch64` |
| Linux: the service fails with "Access to the path is denied" | The `trakhound` user can't write to a folder, ex. after files were copied in by hand. Run `install.sh` again; it resets the owner of the program and data folders |
| Linux: `systemctl status` shows **activating** and then a timeout | The Instance didn't report that it started within `TimeoutStartSec` (5 minutes). Check `journalctl -u trakhound-instance` for errors |
| Behind a reverse proxy, logs show the proxy's address instead of the client's | Add the proxy's address to **Trusted Proxies**, and check that the proxy sends `X-Forwarded-For` (section 5.3) |
| Linux: Admin pages load behind the proxy but don't respond | The proxy doesn't pass WebSockets. Set the `Upgrade` and `Connection` headers (section 5.3) |
