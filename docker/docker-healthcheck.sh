#!/bin/sh
# TrakHound Instance Docker Health Check
#
# Requests the Instance's Live endpoint (/_health/live) on the ASP.NET Core HTTP Port. The .NET Base Images don't include
# curl, so this uses wget (Alpine), curl, or bash's /dev/tcp (Debian and Ubuntu), whichever is available
#
# The Port is the first http:// URL in ASPNETCORE_URLS (which ASP.NET Core uses instead of ASPNETCORE_HTTP_PORTS when both
# are set), otherwise the first of ASPNETCORE_HTTP_PORTS. The request always goes to 127.0.0.1, whatever host the URL binds

# No filename expansion when the URLs are split (ex. http://*:8472)
set -f

port=""
for url in $(echo "$ASPNETCORE_URLS" | tr ';,' '  '); do
    case "$url" in
        http://*)
            hostport="${url#http://}"
            hostport="${hostport%%/*}"
            port="${hostport##*:}"
            # No port in the URL (ex. http://localhost, or http://[::]) means the HTTP default
            case "$port" in ''|*[!0-9]*) port=80 ;; esac
            break
            ;;
    esac
done

if [ -z "$port" ]; then
    port="${ASPNETCORE_HTTP_PORTS%%[,;]*}"
    port="${port:-8472}"
fi
url="http://127.0.0.1:$port/_health/live"

if command -v wget > /dev/null 2>&1; then
    exec wget -q -T 5 -O /dev/null "$url"
elif command -v curl > /dev/null 2>&1; then
    exec curl -fsS -m 5 -o /dev/null "$url"
elif command -v bash > /dev/null 2>&1; then
    exec bash -c 'exec 3<>"/dev/tcp/127.0.0.1/$0" || exit 1
printf "GET /_health/live HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3
read -r version status rest <&3
[ "$status" = "200" ]' "$port"
fi

echo "No HTTP client (wget, curl or bash) available for the health check" >&2
exit 1
