#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

PRIMARY_NODE=$1     # New primary node passed by Pgpool-II
FAILED_NODE=$2      # Failed primary node passed by Pgpool-II
PGDATA="/bitnami/postgresql"  # PostgreSQL data directory
REPLICATION_USER="replication"
REPLICATION_PASSWORD="rep_password"
PGPORT=5432

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

stop_postgres() {
    local node=$1
    log "Stopping PostgreSQL on $node..."
    ssh "$node" "docker exec postgres_primary pg_ctl stop -D $PGDATA -m fast"
}

rewind_node() {
    local source=$1
    local target=$2

    log "Starting pg_rewind to resynchronize $target with $source..."

    ssh "$target" "docker exec postgres_primary pg_rewind \
        --source-server=\"host=$source port=$PGPORT user=$REPLICATION_USER password=$REPLICATION_PASSWORD dbname=postgres\" \
        --target-pgdata=$PGDATA"

    log "pg_rewind completed for $target."
}

start_as_replica() {
    local node=$1
    log "Starting $node as a replica..."

    ssh "$node" "docker exec postgres_primary rm -f $PGDATA/recovery.conf"
    ssh "$node" "docker exec postgres_primary bash -c 'cat <<EOF > $PGDATA/recovery.conf
standby_mode = 'on'
primary_conninfo = 'host=$PRIMARY_NODE port=$PGPORT user=$REPLICATION_USER password=$REPLICATION_PASSWORD'
trigger_file = '/tmp/postgresql.trigger.5432'
EOF'"

    ssh "$node" "docker exec postgres_primary pg_ctl start -D $PGDATA"
    log "$node started as a replica."
}

update_pgpool_status() {
    log "Notifying Pgpool-II about the updated cluster status..."
    docker exec pgpool pcp_attach_node -h localhost -U pgpool -n $FAILED_NODE
}

resynchronize_failed_node() {
    stop_postgres "$FAILED_NODE"
    rewind_node "$PRIMARY_NODE" "$FAILED_NODE"
    start_as_replica "$FAILED_NODE"
    update_pgpool_status
}

main() {
    log "Starting automated resynchronization process..."
    resynchronize_failed_node
    log "Resynchronization process completed successfully."
}

main "$@"
