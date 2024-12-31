#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

PRIMARY_CONTAINER="postgres_primary"
REPLICA_CONTAINER="postgres_replica"
PG_USER="postgres"
PGPASSWORD="your_actual_password"

export PGPASSWORD

# Logger function
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Function to execute SQL on a container
run_psql() {
    local container=$1
    local query=$2

    log "Running query on $container: $query"
    output=$(docker exec "$container" psql -U "$PG_USER" -tAc "$query" 2>&1)
    if [[ $? -ne 0 ]]; then
        log "ERROR: Query failed on $container"
        log "Output: $output"
        exit 1
    fi
    log "Query output: $output"
    echo "$output"
}

# Function to check container status
check_container_status() {
    local container=$1

    if ! docker ps --filter "name=$container" --format '{{.Names}}' | grep -q "$container"; then
        log "ERROR: Container $container is not running."
        exit 1
    fi
    log "Container $container is running."
}

# Check replication status on primary
check_primary_replication_status() {
    log "Checking replication status on primary ($PRIMARY_CONTAINER)..."

    result=$(run_psql "$PRIMARY_CONTAINER" "SELECT * FROM pg_stat_replication;")
    if [[ -z "$result" ]]; then
        log "ERROR: No replicas connected to the primary."
        return 1
    else
        log "Replication status on primary:"
        echo "$result"
    fi
}

# Check recovery status on replica
check_replica_recovery_status() {
    log "Checking recovery status on replica ($REPLICA_CONTAINER)..."

    result=$(run_psql "$REPLICA_CONTAINER" "SELECT pg_is_in_recovery();")
    if [[ "$result" == "t" ]]; then
        log "Replica is in recovery mode."
    else
        log "ERROR: Replica is not in recovery mode."
        return 1
    fi
}

# Test replication data integrity
test_replication_data() {
    log "Testing data replication..."

    log "Creating test data on primary..."
    run_psql "$PRIMARY_CONTAINER" "CREATE TABLE IF NOT EXISTS test_replication (id SERIAL PRIMARY KEY, value TEXT);"
    run_psql "$PRIMARY_CONTAINER" "INSERT INTO test_replication (value) VALUES ('Replication Test - $(date)');"

    log "Checking data on replica..."
    result=$(run_psql "$REPLICA_CONTAINER" "SELECT * FROM test_replication ORDER BY id DESC LIMIT 1;")
    if [[ -n "$result" ]]; then
        log "Replication is working. Latest data on replica: $result"
    else
        log "ERROR: Data not replicated to the replica."
        return 1
    fi
}

# Perform failover and recovery
perform_failover_test() {
    log "Starting failover test..."

    log "Promoting replica ($REPLICA_CONTAINER) to primary..."
    docker exec "$REPLICA_CONTAINER" pg_ctl promote
    sleep 5

    expected_result="Failover Test - $(date)"
    log "Inserting new data on the promoted replica..."
    run_psql "$REPLICA_CONTAINER" "INSERT INTO test_replication (value) VALUES ('$expected_result');"

    log "Verifying data on the new primary..."
    result=$(run_psql "$REPLICA_CONTAINER" "SELECT * FROM test_replication ORDER BY id DESC LIMIT 1;")
    if [[ "$result" == "$expected_result" ]]; then
        log "Data insertion verified on new primary: $result"
    else
        log "ERROR: Data verification failed on new primary."
        exit 1
    fi

    log "Restoring original primary ($PRIMARY_CONTAINER) as a replica..."
    docker exec "$PRIMARY_CONTAINER" pg_ctl stop -m fast
    docker exec "$PRIMARY_CONTAINER" bash -c "rm -f /var/lib/postgresql/data/recovery.done"
    docker exec "$PRIMARY_CONTAINER" pg_rewind --target-pgdata=/var/lib/postgresql/data --source-server="host=$REPLICA_CONTAINER user=$PG_USER password=$PGPASSWORD port=5432"
    docker exec "$PRIMARY_CONTAINER" pg_ctl start

    log "Checking data consistency in the original primary after recovery..."
    result=$(run_psql "$PRIMARY_CONTAINER" "SELECT * FROM test_replication ORDER BY id DESC LIMIT 1;")
    if [[ "$result" == "$expected_result" ]]; then
        log "SUCCESS: Data on original primary matches new primary. No data loss occurred."
    else
        log "ERROR: Data mismatch detected. Possible data loss."
        log "Expected: $expected_result"
        log "Found: $result"
        exit 1
    fi

    log "Failover and recovery test completed successfully!"
}

# Main function
main() {
    log "Starting comprehensive PostgreSQL replication and failover test..."

    log "Checking containers..."
    check_container_status "$PRIMARY_CONTAINER"
    check_container_status "$REPLICA_CONTAINER"

    check_primary_replication_status
    check_replica_recovery_status
    test_replication_data
    perform_failover_test

    log "All tests completed successfully!"
}

# Execute main function
main "$@"
