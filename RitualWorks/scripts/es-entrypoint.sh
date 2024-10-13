#!/bin/bash
set -e

# Copy certificates from /certs-volume to /usr/share/elasticsearch/config
cp /certs-volume/ca.crt /usr/share/elasticsearch/config/ca.crt
cp /certs-volume/es-node-1.ritualworks.com.crt /usr/share/elasticsearch/config/es-node-1.ritualworks.com.crt
cp /certs-volume/es-node-1.ritualworks.com.key /usr/share/elasticsearch/config/es-node-1.ritualworks.com.key
cp /certs-volume/es-node-2.ritualworks.com.crt /usr/share/elasticsearch/config/es-node-2.ritualworks.com.crt
cp /certs-volume/es-node-2.ritualworks.com.key /usr/share/elasticsearch/config/es-node-2.ritualworks.com.key

# Set appropriate permissions
chmod 644 /usr/share/elasticsearch/config/*.crt
chmod 600 /usr/share/elasticsearch/config/*.key

# Execute the original entrypoint
exec /usr/local/bin/docker-entrypoint.sh "$@"
