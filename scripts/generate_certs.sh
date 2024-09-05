#!/bin/bash

# Exit script on any command failure
set -e

# Load environment variables from .env file if present
if [ -f .env ]; then
    set -a
    source .env
    set +a
fi

# Configuration variables
CERT_DIR="${CERT_DIR:-../ssl/certs}"  # Default certificate directory
MINIO_CERT_DIR="${MINIO_CERT_DIR:-/etc/minio/certs}"  # Custom MinIO certificate directory, update if needed
LOG_FILE="${LOG_FILE:-../ssl/cert_generation.log}"
DOMAIN="${DOMAIN:-local.ritualswork.com}"
PROD_DOMAIN="${PROD_DOMAIN:-ritualswork.com}"
EMAIL="${EMAIL:-admin@ritualswork.com}"  # Email for notifications
KEYSTORE_PASSWORD="${KEYSTORE_PASSWORD:-changeit}"
QTRADER_DOMAIN="${QTRADER_DOMAIN:-qtrader.local.ritualswork.com}"
API_DOMAIN="${API_DOMAIN:-api.local.ritualswork.com}"

# Validate required environment variables
if [ -z "$DOMAIN" ] || [ -z "$PROD_DOMAIN" ] || [ -z "$EMAIL" ]; then
    echo "ERROR: One or more critical environment variables are missing." | tee -a "$LOG_FILE"
    exit 1
fi

# Log messages to file and console
log_message() {
    local message="$1"
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message" | tee -a "$LOG_FILE"
}

# Handle errors and exit
error_exit() {
    local message="$1"
    log_message "ERROR: $message"
    exit 1
}

# Set permissions on certificate files
set_permissions() {
    local file="$1"
    if [ -f "$file" ]; then
        sudo chmod 600 "$file" || error_exit "Failed to set permissions for $file"
        log_message "Permissions set for $file"
    else
        error_exit "File $file does not exist"
    fi
}

# Set permissions for all files in the certificates directory
set_permissions_for_all_certs() {
    log_message "Setting permissions for all certificate files in $CERT_DIR..."
    sudo find "$CERT_DIR" -type f -exec chmod 600 {} \; || error_exit "Failed to set permissions for certificates in $CERT_DIR"
    log_message "Permissions set for all certificate files."
}

# Generate CA certificate
generate_ca_cert() {
    log_message "Generating CA certificate..."
    mkdir -p "$CERT_DIR" || error_exit "Failed to create directory $CERT_DIR"
    
    if openssl genpkey -algorithm RSA -out "$CERT_DIR/ca.key" -pkeyopt rsa_keygen_bits:2048; then
        log_message "CA private key generated successfully."
        set_permissions "$CERT_DIR/ca.key"
    else
        error_exit "Error generating CA private key."
    fi

    if openssl req -x509 -new -nodes -key "$CERT_DIR/ca.key" -sha256 -days 3650 -out "$CERT_DIR/ca.crt" -subj "/CN=Ritualworks CA"; then
        log_message "CA certificate generated successfully."
        set_permissions "$CERT_DIR/ca.crt"
    else
        error_exit "Error generating CA certificate."
    fi
}

# Generate self-signed certificates for a service
generate_self_signed_cert() {
    local service="$1"
    local cert_file="$CERT_DIR/$service.crt"
    local key_file="$CERT_DIR/$service.key"
    local pem_file="$CERT_DIR/$service.pem"
    local san

    case "$service" in
        "es-node-1")
            san="DNS:es-node-1"
            ;;
        "es-node-2")
            san="DNS:es-node-2"
            ;;
        "rabbitmq-node1")
            san="DNS:rabbitmq-node1"
            ;;
        "rabbitmq-node2")
            san="DNS:rabbitmq-node2"
            ;;
        "minio1" | "minio2" | "minio3" | "minio4")
            # For MinIO, generate the files directly as public.crt and private.key
            cert_file="$CERT_DIR/public.crt"
            key_file="$CERT_DIR/private.key"
            san="DNS:minio"
            ;;
        "postgres" | "redis" | "haproxy")
            san="DNS:$service"
            ;;
        *)
            san="DNS:$service"
            ;;
    esac

    log_message "Generating self-signed certificate for $service..."
    mkdir -p "$CERT_DIR" || error_exit "Failed to create directory $CERT_DIR"

    if openssl req -new -nodes -newkey rsa:2048 -keyout "$key_file" -out "$CERT_DIR/$service.csr" -subj "/CN=$service" &&
       openssl x509 -req -in "$CERT_DIR/$service.csr" -CA "$CERT_DIR/ca.crt" -CAkey "$CERT_DIR/ca.key" -CAcreateserial -out "$cert_file" -days 365 -sha256 -extfile <(printf "subjectAltName=%s" "$san"); then
        log_message "Self-signed certificate for $service generated successfully."
        set_permissions "$key_file"
        set_permissions "$cert_file"
        # Combine the certificate and key into a .pem file
        cat "$cert_file" "$key_file" > "$pem_file"
        set_permissions "$pem_file"
    else
        error_exit "Error generating self-signed certificate for $service."
    fi
}

# Copy certificates to MinIO certs directory
copy_minio_certs() {
    log_message "Copying MinIO certificates to $MINIO_CERT_DIR..."
    mkdir -p "$MINIO_CERT_DIR" || error_exit "Failed to create MinIO certificate directory"

    # Copy the certificates specifically named as public.crt and private.key
    cp "$CERT_DIR/public.crt" "$MINIO_CERT_DIR/public.crt" || error_exit "Failed to copy MinIO public certificate"
    cp "$CERT_DIR/private.key" "$MINIO_CERT_DIR/private.key" || error_exit "Failed to copy MinIO private key"
    set_permissions "$MINIO_CERT_DIR/public.crt"
    set_permissions "$MINIO_CERT_DIR/private.key"
    log_message "MinIO certificates copied successfully."
}

# Cleanup redundant MinIO certificates
cleanup_minio_redundant_certs() {
    log_message "Removing redundant MinIO certificates..."
    rm -f "$CERT_DIR/minio1.crt" "$CERT_DIR/minio1.key" \
          "$CERT_DIR/minio2.crt" "$CERT_DIR/minio2.key" \
          "$CERT_DIR/minio3.crt" "$CERT_DIR/minio3.key" \
          "$CERT_DIR/minio4.crt" "$CERT_DIR/minio4.key" || error_exit "Failed to remove redundant MinIO certificates"
    log_message "Redundant MinIO certificates removed successfully."
}

# Generate self-signed certificates for frontend services
generate_self_signed_cert_frontend() {
    local domain="$1"
    local cert_file="$CERT_DIR/$domain.crt"
    local key_file="$CERT_DIR/$domain.key"
    local pem_file="$CERT_DIR/$domain.pem"

    log_message "Generating self-signed certificate for $domain..."
    mkdir -p "$CERT_DIR" || error_exit "Failed to create directory $CERT_DIR"
    if openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout "$key_file" -out "$cert_file" -subj "/CN=$domain/O=Self-Signed Certificate"; then
        log_message "Self-signed certificate for $domain generated successfully."
        set_permissions "$key_file"
        set_permissions "$cert_file"
        # Combine the certificate and key into a .pem file
        cat "$cert_file" "$key_file" > "$pem_file"
        set_permissions "$pem_file"
    else
        error_exit "Error generating self-signed certificate for $domain."
    fi
}

# Function to obtain Let's Encrypt certificates
generate_lets_encrypt_cert() {
    local domain="$1"
    local cert_file="$CERT_DIR/$domain.crt"
    local key_file="$CERT_DIR/$domain.key"
    local pem_file="$CERT_DIR/$domain.pem"

    log_message "Generating Let's Encrypt certificate for $domain..."
    if certbot certonly --standalone --non-interactive --agree-tos --email $EMAIL -d $domain -d $QTRADER_DOMAIN -d $API_DOMAIN --cert-name $domain; then
        cp /etc/letsencrypt/live/$domain/fullchain.pem "$cert_file"
        cp /etc/letsencrypt/live/$domain/privkey.pem "$key_file"
        set_permissions "$key_file"
        set_permissions "$cert_file"
        log_message "Let's Encrypt certificate for $domain generated and copied successfully."

        # Combine the certificate and key into a .pem file
        cat "$cert_file" "$key_file" > "$pem_file"
        set_permissions "$pem_file"
    else
        error_exit "Error generating Let's Encrypt certificate for $domain."
    fi
}

# Main script execution
mkdir -p "$(dirname "$LOG_FILE")" || error_exit "Failed to create log directory"
generate_ca_cert

# Verify CA certificate exists
if [ ! -f "$CERT_DIR/ca.crt" ]; then
    error_exit "Required certificate ca.crt not found. Certificate generation did not complete successfully."
fi

# List of backend services to generate certificates for
backend_services=("postgres" "redis" "rabbitmq-node1" "rabbitmq-node2" "es-node-1" "es-node-2" "minio1" "minio2" "minio3" "minio4" "haproxy")
for service in "${backend_services[@]}"; do
    generate_self_signed_cert "$service"
done

# Copy MinIO-specific certificates to the required location
copy_minio_certs

# Clean up redundant MinIO certificates
cleanup_minio_redundant_certs

# Set permissions for all certificates
set_permissions_for_all_certs

# Determine the operating system and act accordingly
OS="$(uname -s)"
case "$OS" in
    Linux*)
        if [ "$ENVIRONMENT" == "production" ]; then
            generate_lets_encrypt_cert $DOMAIN
        else
            generate_self_signed_cert_frontend $DOMAIN
            generate_self_signed_cert_frontend $QTRADER_DOMAIN
            generate_self_signed_cert_frontend $API_DOMAIN
        fi
        ;;
    Darwin*)
        generate_self_signed_cert_frontend $DOMAIN
        generate_self_signed_cert_frontend $QTRADER_DOMAIN
        generate_self_signed_cert_frontend $API_DOMAIN
        ;;
    *)
        error_exit "Unsupported operating system: $OS"
        ;;
esac

log_message "Certificate generation completed."

# Display the generated certificates
log_message "Generated certificates:"
ls -l "$CERT_DIR" | tee -a "$LOG_FILE"
