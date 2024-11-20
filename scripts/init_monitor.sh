#!/bin/bash
set -e
# Change ownership of PGDATA

# Environment Variables
export PGDATA="/var/lib/postgresql/data"
export CERT_DIR="/certs-volume"
export MONITOR_HOSTNAME="pg_monitor"

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

log "Starting pg_auto_failover monitor initialization..."

# Check for SSL certificates
if [ -f "$CERT_DIR/postgres.haworks.com.crt" ] && [ -f "$CERT_DIR/postgres.haworks.com.key" ]; then
    log "SSL certificates found. Configuring SSL for monitor..."
    cp "$CERT_DIR/postgres.haworks.com.crt" "$PGDATA/server.crt"
    cp "$CERT_DIR/postgres.haworks.com.key" "$PGDATA/server.key"
    chmod 600 "$PGDATA/server.crt" "$PGDATA/server.key"
    chown postgres:postgres "$PGDATA/server.crt" "$PGDATA/server.key"
    USE_SSL=true
else
    log "SSL certificates not found. Proceeding without SSL."
    USE_SSL=false
fi

# Initialize the monitor
if [ "$USE_SSL" = true ]; then
    log "Initializing monitor with SSL and md5 authentication..."
    pg_autoctl create monitor \
        --pgdata "$PGDATA" \
        --ssl-mode require \
        --server-cert "$PGDATA/server.crt" \
        --server-key "$PGDATA/server.key" \
        --auth md5 \
        --hostname "$MONITOR_HOSTNAME"
else
    log "Initializing monitor without SSL and with md5 authentication..."
    pg_autoctl create monitor \
        --pgdata "$PGDATA" \
        --auth md5 \
        --no-ssl \
        --hostname "$MONITOR_HOSTNAME"
fi

# Start pg_autoctl
log "Starting pg_autoctl..."
pg_autoctl run
