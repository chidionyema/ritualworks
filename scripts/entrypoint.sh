#!/bin/bash
set -euo pipefail

# Log with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Path to the CA certificate within the container (mounted via volume)
CA_CERT_VOLUME_PATH="/certs-volume/ca.crt"
# System location where CA certificates are stored (Alpine Linux)
SYSTEM_CA_CERT_PATH="/usr/local/share/ca-certificates/ca.crt"

# Install the CA certificate *only if it's different* from the existing one.
if [ -f "$CA_CERT_VOLUME_PATH" ]; then
  if ! cmp -s "$CA_CERT_VOLUME_PATH" "$SYSTEM_CA_CERT_PATH"; then  # Use cmp -s for silent comparison
    log "Installing CA certificate from $CA_CERT_VOLUME_PATH..."
    cp "$CA_CERT_VOLUME_PATH" "$SYSTEM_CA_CERT_PATH"
    update-ca-certificates  # Update the system's CA certificate store
  else
    log "CA certificate is already up-to-date."
  fi
else
  log "CA certificate not found in $CA_CERT_VOLUME_PATH.  Connections to TLS services may fail."
fi

log "Starting application..."
exec dotnet haworks.dll