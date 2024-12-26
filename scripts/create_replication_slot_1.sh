#!/bin/bash
set -e

echo "Checking if replication slot exists..."

# Define slot name
SLOT_NAME="replica_slot_1"

# Export PostgreSQL password
export PGPASSWORD="$POSTGRESQL_PASSWORD"

# Query to check if slot exists
SLOT_EXISTS_QUERY="SELECT slot_name FROM pg_replication_slots WHERE slot_name = '$SLOT_NAME';"

# Query to create the slot
CREATE_SLOT_QUERY="SELECT * FROM pg_create_physical_replication_slot('$SLOT_NAME');"

# Check if slot exists, if not, create it
SLOT_EXISTS=$(psql -U "$POSTGRESQL_USERNAME" -h localhost -d postgres -tAc "$SLOT_EXISTS_QUERY")
if [[ -z "$SLOT_EXISTS" ]]; then
    echo "Replication slot '$SLOT_NAME' does not exist. Creating..."
    psql -U "$POSTGRESQL_USERNAME" -h localhost -d postgres -c "$CREATE_SLOT_QUERY"
    echo "Replication slot '$SLOT_NAME' created successfully."
else
    echo "Replication slot '$SLOT_NAME' already exists."
fi
