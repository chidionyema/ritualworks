#!/bin/bash
set -euo pipefail

# Log with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Path to the CA certificate within the container
CA_CERT_VOLUME_PATH="/certs-volume/ca.crt"
SYSTEM_CA_CERT_PATH="/usr/local/share/ca-certificates/ca.crt"

# Install the CA certificate and verify it worked
if [ -f "$CA_CERT_VOLUME_PATH" ]; then
  log "Installing CA certificate from $CA_CERT_VOLUME_PATH..."
  
  # Copy and make sure it's readable
  cp "$CA_CERT_VOLUME_PATH" "$SYSTEM_CA_CERT_PATH"
  chmod 644 "$SYSTEM_CA_CERT_PATH"
  
  # Update the CA store
  update-ca-certificates
  
  # Verify the installation
  if [ -f "/etc/ssl/certs/ca.pem" ] || grep -q "MyRootCA" /etc/ssl/certs/ca-certificates.crt; then
    log "CA certificate successfully installed."
  else
    log "WARNING: CA certificate installation could not be verified."
  fi
else
  log "ERROR: CA certificate not found in $CA_CERT_VOLUME_PATH. Connections to TLS services may fail."
  exit 1
fi

# Validate that we can connect to Vault (optional test)
log "Testing connection to Vault..."
if curl -s --cacert "$CA_CERT_VOLUME_PATH" https://vault:8200/v1/sys/health; then
  log "Successfully connected to Vault."
else
  log "WARNING: Could not connect to Vault. Certificate issues may persist."
fi

log "Starting application..."
exec dotnet haworks.dll