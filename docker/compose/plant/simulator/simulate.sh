#!/bin/sh
# Simulated machines : publishes readings for each machine on each line to the MQTT Broker every INTERVAL seconds, as
#   plant/<line>/<machine>/temperature   {"value": 64.2}     (degrees, rising and falling over about 10 minutes)
#   plant/<line>/<machine>/spindle_speed {"value": 8012.5}   (RPM, 0 while the machine is stopped)
#   plant/<line>/<machine>/running       {"value": true}     (stops for about a minute every 5 minutes)
# A real plant would have the machines (or a gateway) publish these instead

broker="${MQTT_HOSTNAME:-mosquitto}"
interval="${INTERVAL:-1}"
lines="${LINES:-line1 line2}"
machines="${MACHINES:-press-1 press-2}"

echo "Publishing readings for lines '$lines', machines '$machines' to $broker every ${interval}s"

tick=0
while true; do
    index=0
    for line in $lines; do
        for machine in $machines; do
            index=$((index + 1))

            # Each machine has its own offset, so they don't all read the same
            values=$(awk -v t="$tick" -v i="$index" 'BEGIN {
                srand(t * 100 + i)
                running = ((t + i * 37) % 300) >= 60
                temperature = 55 + i * 3 + 8 * sin((t + i * 50) / 95.5) + rand() * 1.5
                speed = running ? 8000 + i * 250 + rand() * 40 - 20 : 0
                printf "%.1f %.1f %s", temperature, speed, running ? "true" : "false"
            }')
            set -- $values

            mosquitto_pub -h "$broker" -t "plant/$line/$machine/temperature" -m "{\"value\": $1}" || echo "Publish to $broker failed"
            mosquitto_pub -h "$broker" -t "plant/$line/$machine/spindle_speed" -m "{\"value\": $2}"
            mosquitto_pub -h "$broker" -t "plant/$line/$machine/running" -m "{\"value\": $3}"
        done
    done

    tick=$((tick + interval))
    sleep "$interval"
done
