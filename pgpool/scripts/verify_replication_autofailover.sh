#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

PRIMARY_HOST="${PRIMARY_HOST:-postgres_primary}"
REPLICA_HOST="${REPLICA_HOST:-postgres_replica}"
PG_USER="postgres"
PGPASSWORD="your_actual_password" 

export PGPASSWORD

# Logger function
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Function to check PostgreSQL availability
check_postgres_status() {
    local host=$1
    local port=$2

    log "Checking PostgreSQL availability at $host:$port..."
    if ! pg_isready -h "$host" -p "$port" -U "$PG_USER"; then
        log "ERROR: PostgreSQL at $host:$port is not available."
        exit 1
    fi
    log "PostgreSQL at $host:$port is running."
}

# Function to execute SQL
run_psql() {
    local host=$1
    local query=$2

    log "Running query on $host: $query"
    output=$(psql -h "$host" -U "$PG_USER" -tAc "$query" 2>&1)
    if [[ $? -ne 0 ]]; then
        log "ERROR: Query failed on $host"
        log "Output: $output"
        exit 1
    fi
    log "Query output: $output"
    echo "$output"
}

# Check replication status on primary
check_primary_replication_status() {
    log "Checking replication status on primary ($PRIMARY_HOST)..."

    result=$(run_psql "$PRIMARY_HOST" "SELECT * FROM pg_stat_replication;")
    if [[ -z "$result" ]]; then
        log "ERROR: No replicas connected to the primary."
        exit 1
    else
        log "Replication status on primary:"
        echo "$result"
    fi
}

# Check recovery status on replica with retries
check_replica_recovery_status() {
    log "Checking recovery status on replica ($REPLICA_HOST)..."
    max_retries=20
    retry_interval=5
    attempt=1

    while [[ $attempt -le $max_retries ]]; do
        result=$(run_psql "$REPLICA_HOST" "SELECT pg_is_in_recovery();")
        log "Raw query output: '$result'"

        # Check if the output contains "t" (true)
        if grep -q "t" <<< "$result"; then 
            log "Replica is in recovery mode."
            return 0
        fi

        log "Attempt $attempt/$max_retries: Replica is not in recovery mode. Retrying in $retry_interval seconds..."
        sleep $retry_interval
        ((attempt++))
    done

    log "ERROR: Replica recovery mode check failed after $max_retries attempts."
    exit 1
}

# Test replication data integrity
test_replication_data() {
    log "Testing data replication..."

    log "Creating test data on primary..."
    run_psql "$PRIMARY_HOST" "CREATE TABLE IF NOT EXISTS test_replication (id SERIAL PRIMARY KEY, value TEXT);"
    run_psql "$PRIMARY_HOST" "INSERT INTO test_replication (value) VALUES ('Replication Test - $(date)');"

    log "Checking data on replica..."
    result=$(run_psql "$REPLICA_HOST" "SELECT * FROM test_replication ORDER BY id DESC LIMIT 1;")
    if [[ -n "$result" ]]; then
        log "Replication is working. Latest data on replica: $result"
    else
        log "ERROR: Data not replicated to the replica."
        exit 1
    fi
}

# Perform failover and recovery
perform_failover_test() {
    log "Starting failover test..."

    log "Promoting replica ($REPLICA_HOST) to primary..."
    run_psql "$REPLICA_HOST" "SELECT pg_promote();"
    sleep 10 # Increased sleep time 

    expected_result="Failover Test - $(date)"
    log "Inserting new data on the promoted replica..."
    run_psql "$REPLICA_HOST" "INSERT INTO test_replication (value) VALUES ('$expected_result');"

    log "Verifying data on the new primary..."
    # Add a delay to allow replication to catch up
    sleep 5 
    result=$(run_psql "$REPLICA_HOST" "SELECT value FROM test_replication ORDER BY id DESC LIMIT 1;") 
    if [[ "$result" == "$expected_result" ]]; then 
        log "Data insertion verified on new primary: $result"
    else
        log "ERROR: Data verification failed on new primary."
        log "Expected: $expected_result"
        log "Actual: $result" 
        exit 1
    fi

    log "Restoring original primary ($PRIMARY_HOST) as a replica..."
    run_psql "$PRIMARY_HOST" "SELECT pg_wal_replay_resume();"

    log "Checking data consistency in the original primary after recovery..."
    result=$(run_psql "$PRIMARY_HOST" "SELECT value FROM test_replication ORDER BY id DESC LIMIT 1;")
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

    check_postgres_status "$PRIMARY_HOST" 5432
    check_postgres_status "$REPLICA_HOST" 5432

    check_primary_replication_status
    check_replica_recovery_status
    test_replication_data
    perform_failover_test

    log "All tests completed successfully!"
}

# Execute main function
main "$@"