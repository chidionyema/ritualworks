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

# Start all services function
start_services() {
    log "Starting all services..."
    ./start_all_services.sh || error_exit "Service startup failed."
}

DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-postgres.yml"
DOCKER_VOLUME="certs-volume"
CERTS_DIR="/certs"
SERVICES=("postgres" "redis" "rabbitmq-node1" "es-node-1" "minio1" "local.haworks.com" "vault" "consul")

generate_certificates() {
    log "Generating TLS certificates for Vault, Consul, and other services..."

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
                openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 \
                    -subj "/CN=MyRootCA" \
                    -out ca.crt
                echo "CA key and certificate generated."
            else
                echo "CA key and certificate already exist. Skipping generation."
            fi

            # Special handling for MinIO: Generate shared public.crt and private.key
            shared_cert_file="/certs-volume/public.crt"
            shared_key_file="/certs-volume/private.key"

            if [ ! -f "$shared_cert_file" ] || [ ! -f "$shared_key_file" ]; then
                echo "Generating shared certificate for MinIO services..."
                CONFIG_FILE="/certs-volume/minio.cnf"

                # Create OpenSSL configuration for MinIO
                cat > "$CONFIG_FILE" <<EOF
[ req ]
default_bits        = 2048
prompt              = no
distinguished_name  = dn
req_extensions      = v3_req

[ dn ]
CN                  = minio

[ v3_req ]
keyUsage            = digitalSignature, keyEncipherment
extendedKeyUsage    = serverAuth, clientAuth
subjectAltName      = @alt_names

[ alt_names ]
DNS.1               = minio1
DNS.2               = minio2
DNS.3               = localhost
DNS.4               = minio.local.haworks.com
IP.1                = 127.0.0.1
EOF

                # Generate private key for MinIO
                openssl genrsa -out "$shared_key_file" 2048
                echo "Private key for MinIO generated: $shared_key_file"

                # Generate CSR for MinIO
                openssl req -new -key "$shared_key_file" -config "$CONFIG_FILE" -out "/certs-volume/minio.csr"
                echo "CSR for MinIO generated: /certs-volume/minio.csr"

                # Sign the CSR to generate the public certificate
                openssl x509 -req -in "/certs-volume/minio.csr" -CA ca.crt -CAkey ca.key -CAcreateserial \
                    -days 365 -sha256 -extensions v3_req -extfile "$CONFIG_FILE" -out "$shared_cert_file"
                echo "Public certificate for MinIO generated: $shared_cert_file"

                # Set permissions for shared MinIO certificates
                chmod 644 "$shared_cert_file"
                chmod 600 "$shared_key_file"
                echo "Shared certificate for MinIO services successfully generated."
            else
                echo "Shared certificate for MinIO already exists."
            fi

            # Standard certificate generation for other services
            SERVICES="postgres redis rabbitmq-node1 es-node-1 local.haworks.com vault consul agent"

            for service in $SERVICES; do
                echo "Generating certificate for $service..."

                CONFIG_FILE="${service}.cnf"
                cat > "$CONFIG_FILE" <<EOF
[ req ]
default_bits        = 2048
prompt              = no
distinguished_name  = dn
req_extensions      = v3_req

[ dn ]
CN                  = $service

[ v3_req ]
keyUsage            = digitalSignature, keyEncipherment
extendedKeyUsage    = serverAuth, clientAuth
subjectAltName      = @alt_names

[ alt_names ]
DNS.1               = $service
DNS.2               = $service.ritualworks.com
DNS.3               = localhost
IP.1                = 127.0.0.1
EOF

                # Additional SANs for specific services
                if [ "$service" = "postgres" ]; then
                    echo "Adding additional SANs for PostgreSQL..."
                    echo "DNS.4 = postgres_primary" >> "$CONFIG_FILE"
                    echo "DNS.5 = postgres_replica" >> "$CONFIG_FILE"
                    echo "DNS.6 = pgpool" >> "$CONFIG_FILE"
                elif [ "$service" = "redis" ]; then
                    echo "Adding additional SANs for Redis..."
                    echo "DNS.4 = redis-master" >> "$CONFIG_FILE"
                fi

                # Generate private key and CSR
                openssl genrsa -out "${service}.key" 2048
                openssl req -new -key "${service}.key" -config "$CONFIG_FILE" -out "${service}.csr"
                echo "CSR for $service generated: ${service}.csr"

                # Sign the CSR with the CA
                openssl x509 -req -in "${service}.csr" -CA ca.crt -CAkey ca.key -CAcreateserial \
                    -days 365 -sha256 -extensions v3_req -extfile "$CONFIG_FILE" -out "${service}.crt"
                echo "Public certificate for $service generated: ${service}.crt"

                # Set permissions
                if [ "$service" = "postgres" ]; then
                    chmod 640 "${service}.key"
                    chmod 644 "${service}.crt"
                else
                    chmod 644 "${service}.key"
                    chmod 644 "${service}.crt"
                fi

                chown root:root "${service}.key" "${service}.crt"
                echo "Certificate for $service generated successfully."
            done

            # Set permissions for CA files
            chmod 644 ca.crt
            echo "All certificates generated successfully."
        ' || error_exit "Failed to generate certificates for Vault and Consul."

    log "Certificates successfully generated and stored in ${DOCKER_VOLUME}."
}

# Start Postgres function
start_postgres() {
    log "Starting PostgreSQL..."
    docker-compose -p "haworks" -f "$DOCKER_COMPOSE_FILE" up -d || error_exit "Failed to start PostgreSQL."
    log "Waiting for PostgreSQL to start..."
    sleep 5
}

# Main script execution
log "Initializing Vault and Docker services deployment..."

# Step 1: Create Docker networks
log "Creating Docker networks..."
./create_networks.sh || error_exit "Failed to create Docker networks."

# Step 2: Generate certificates
generate_certificates

# Step 3: Deploy Vault
log "Deploying Vault server..."
../vault/scripts/install_vault_server.sh || error_exit "Vault deployment failed."

# Step 4: Configure Vault and PostgreSQL
log "Configuring Vault and PostgreSQL..."
start_postgres
../vault/scripts/configure_vault.sh || error_exit "Vault configuration failed."

# Step 5: Start all services
start_services

log "Deployment completed successfully. All services are now running."
