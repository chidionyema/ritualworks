#!/bin/bash

set -e

VAULT_CONTAINER_NAME="ritualworks-vault-1"
VAULT_ADDR="${VAULT_ADDR:-http://127.0.0.1:8200}"
VAULT_PATH="${VAULT_PATH:-pki}"
CERT_TTL="${CERT_TTL:-8760h}"
ISSUE_TTL="${ISSUE_TTL:-72h}"
ROLE_NAME="${ROLE_NAME:-ritualworks-role}"
SHARED_CERT_DIR="/certs-volume"

SERVICES=("postgres" "redis" "rabbitmq-node1" "rabbitmq-node2" "es-node-1" "es-node-2" "minio1" "minio2")

log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $1"
}

error_exit() {
    log "Error: $1"
    exit 1
}

authenticate_with_root_token() {
    local unseal_keys_file="$1"
    log "Authenticating with Vault using root token..."
    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$unseal_keys_file")
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token from $unseal_keys_file."
    fi
    docker exec "$VAULT_CONTAINER_NAME" vault login -no-print "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault."
}

docker exec "$VAULT_CONTAINER_NAME" mkdir -p "$SHARED_CERT_DIR" || error_exit "Failed to create shared cert directory."

enable_pki_engine() {
    log "Enabling PKI secrets engine at $VAULT_PATH..."
    if docker exec "$VAULT_CONTAINER_NAME" vault secrets list | grep -q "^$VAULT_PATH/"; then
        log "PKI engine already enabled."
    else
        docker exec "$VAULT_CONTAINER_NAME" vault secrets enable -path="$VAULT_PATH" pki || error_exit "Failed to enable PKI."
    fi
}

configure_pki_engine() {
    log "Configuring PKI engine max TTL to $CERT_TTL..."
    docker exec "$VAULT_CONTAINER_NAME" vault secrets tune -max-lease-ttl="$CERT_TTL" "$VAULT_PATH" || error_exit "Failed to configure PKI."
}

generate_root_ca() {
    log "Generating root CA..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/root/generate/internal" \
        common_name="Ritualworks Root CA" \
        ttl="$CERT_TTL" || error_exit "Failed to generate root CA."
}

configure_urls() {
    log "Configuring certificate issuance URLs..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/config/urls" \
        issuing_certificates="$VAULT_ADDR/v1/$VAULT_PATH/ca" \
        crl_distribution_points="$VAULT_ADDR/v1/$VAULT_PATH/crl" || error_exit "Failed to configure URLs."
}

create_vault_role() {
    log "Creating Vault role $ROLE_NAME..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/roles/$ROLE_NAME" \
        allowed_domains="ritualworks.com" \
        allow_subdomains=true \
        max_ttl="$ISSUE_TTL" || error_exit "Failed to create Vault role."
}

request_cert() {
    local service="$1"
    local domain="${service}.ritualworks.com"
    local sans=""

    case "$service" in
        "rabbitmq-node1"|"rabbitmq-node2")
            sans="san=DNS:rabbitmq-node1.ritualworks.com,DNS:rabbitmq-node2.ritualworks.com"
            ;;
        "es-node-1"|"es-node-2")
            sans="san=DNS:es-node-1.ritualworks.com,DNS:es-node-2.ritualworks.com"
            ;;
        "minio1"|"minio2")
            sans="san=DNS:minio1,DNS:minio2"
            ;;
        *)
            sans="san=DNS:${domain}"
            ;;
    esac

    local shared_cert_file="$SHARED_CERT_DIR/${domain}.crt"
    local shared_key_file="$SHARED_CERT_DIR/${domain}.key"

    # Specific certificate file names for MinIO
    if [[ "$service" == "minio1" || "$service" == "minio2" ]]; then
        shared_cert_file="$SHARED_CERT_DIR/public.crt"
        shared_key_file="$SHARED_CERT_DIR/private.key"
    fi

    log "Requesting certificate for $domain with SANs: $sans"

    docker exec "$VAULT_CONTAINER_NAME" sh -c "\
        response=\$(vault write -format=json $VAULT_PATH/issue/$ROLE_NAME common_name=$domain $sans ttl=$ISSUE_TTL) && \
        echo \"\$response\" | jq -r '.data.certificate' > $shared_cert_file && \
        echo \"\$response\" | jq -r '.data.private_key' > $shared_key_file" || error_exit "Failed to request certificate."

    log "Certificate for $service saved with SANs in $shared_cert_file and $shared_key_file."
}

export_root_ca() {
    local ca_cert_file="$SHARED_CERT_DIR/ca.crt"
    log "Exporting Root CA certificate to $ca_cert_file..."
    docker exec "$VAULT_CONTAINER_NAME" sh -c "\
        vault read -field=certificate $VAULT_PATH/cert/ca > $ca_cert_file" || error_exit "Failed to export Root CA."
}

main() {
    UNSEAL_KEYS_FILE="unseal_keys.json"
    if [[ ! -f "$UNSEAL_KEYS_FILE" ]]; then
        error_exit "Unseal keys file not found: $UNSEAL_KEYS_FILE"
    fi

    authenticate_with_root_token "$UNSEAL_KEYS_FILE"
    enable_pki_engine
    configure_pki_engine
    generate_root_ca
    configure_urls
    create_vault_role
    export_root_ca

    for service in "${SERVICES[@]}"; do
        request_cert "$service"
    done

    log "Certificates have been successfully generated and stored."
}

main
