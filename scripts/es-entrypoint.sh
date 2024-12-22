#!/bin/bash
set -e  # Exit immediately if a command exits with a non-zero status



# Copy certificates from /certs-volume to /usr/share/elasticsearch/config
cp /certs-volume/ca.crt /usr/share/elasticsearch/config/ca.crt || {
  echo "Error: Could not copy ca.crt"
  exit 1
}
cp /certs-volume/es-node-1.crt /usr/share/elasticsearch/config/es-node-1.crt || {
  echo "Error: Could not copy es-node-1.crt"
  exit 1
}
cp /certs-volume/es-node-1.key /usr/share/elasticsearch/config/es-node-1.key || {
  echo "Error: Could not copy es-node-1.key"
  exit 1
}

# Set appropriate permissions for the copied certificates
chmod 644 /usr/share/elasticsearch/config/*.crt || {
  echo "Error: Could not set permissions on CRT files"
  exit 1
}
chmod 600 /usr/share/elasticsearch/config/*.key || {
  echo "Error: Could not set permissions on KEY files"
  exit 1
}

# Execute the original entrypoint script
exec /usr/local/bin/docker-entrypoint.sh "$@"


es-node-1.key
es-node-1.crt



