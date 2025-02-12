#!/bin/bash
set -euo pipefail

# Log with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Install the CA certificate from the certs-volume if available.
if [ -f "/certs-volume/ca.crt" ]; then
  log "Installing CA certificate from /certs-volume/ca.crt..."
  cp /certs-volume/ca.crt /usr/local/share/ca-certificates/ca.crt && update-ca-certificates
else
  log "CA certificate not found in /certs-volume. Skipping update."
fi

log "Starting application..."
# Use the correct assembly name (haworks.dll in your case)
exec dotnet haworks.dll
