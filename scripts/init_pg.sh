#!/bin/bash

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
} 

log "Starting PostgreSQL initialization script..."

../scripts/init_pgress.sh