#!/bin/bash
set -e

echo "Stopping primary PostgreSQL container..."
docker stop postgres_primary

echo "Restoring backup using pgBackRest..."
docker exec pgbackrest pgbackrest --stanza=main --log-level-console=info restore

echo "Starting primary PostgreSQL container..."
docker start postgres_primary

echo "Reinitializing replica..."
docker stop postgres_replica
docker exec -it postgres_replica rm -rf /bitnami/postgresql/*
docker start postgres_replica

echo "Restore process completed successfully."
