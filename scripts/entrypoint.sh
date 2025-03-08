#!/bin/bash
set -euo pipefail

# Log with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Path to the CA certificate within the container
CA_CERT_VOLUME_PATH="/certs-volume/ca.crt"
SYSTEM_CA_CERT_PATH="/usr/local/share/ca-certificates/ca.crt"

# Install the CA certificate
if [ -f "$CA_CERT_VOLUME_PATH" ]; then
  log "Installing CA certificate from $CA_CERT_VOLUME_PATH..."
  
  # Copy and make sure it's readable
  cp "$CA_CERT_VOLUME_PATH" "$SYSTEM_CA_CERT_PATH"
  chmod 644 "$SYSTEM_CA_CERT_PATH"
  
  # Update the CA store
  update-ca-certificates
  
  # Don't rely on specific file names or content for verification
  log "CA certificate successfully installed."
else
  log "ERROR: CA certificate not found in $CA_CERT_VOLUME_PATH. Connections to TLS services may fail."
  exit 1
fi


# Set environment variable to trust the system cert store
export DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0
export DOTNET_SSL_ENABLE_CERTIFICATE_VALIDATION=true

log "Starting application..."
exec dotnet haworks.dll