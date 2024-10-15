#!/bin/bash

# Stop PostgreSQL before restoration
pg_ctl stop

# Restore from the latest backup
pgbackrest --stanza=pg-stanza restore

# Start PostgreSQL
pg_ctl start
