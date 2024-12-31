#!/bin/bash

set -e  # Exit immediately if any command fails

# Logging function
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handler
error_exit() {
    log "Error: $1"
    exit 1
}

DOCKER_VOLUME="certs-volume"

log "Generating TLS certificates for services..."

docker run --rm \
    -v "${DOCKER_VOLUME}:/certs-volume" \
    -w "/certs-volume" \
    alpine sh -c '
        set -e
        apk update
        apk add --no-cache openssl

        # Generate CA key and certificate if not already present
        if [ ! -f ca.key ] || [ ! -f ca.crt ]; then
            echo "Generating CA key and certificate..."
            openssl genrsa -out ca.key 4096
            opens
