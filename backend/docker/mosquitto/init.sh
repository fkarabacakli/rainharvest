#!/bin/sh
# Builds the Mosquitto password file from environment variables, then starts the broker.
set -eu

passwd_file=/mosquitto/data/passwd

rm -f "$passwd_file"
mosquitto_passwd -c -b "$passwd_file" "$MQTT_API_USER" "$MQTT_API_PASSWORD"
mosquitto_passwd -b "$passwd_file" "$MQTT_SIM_USER" "$MQTT_SIM_PASSWORD"
chown mosquitto:mosquitto "$passwd_file"
chmod 0700 "$passwd_file"

exec mosquitto -c /mosquitto/config/mosquitto.conf
