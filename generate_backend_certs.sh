#!/bin/bash

# Configuration
CERT_DIR="${CERT_DIR:-./ssl/certs}"
LOG_FILE="${LOG_FILE:-./ssl/cert_generation.log}"
DOMAIN="local.ritualworks.com"
PROD_DOMAIN="ritualworks.com"
EMAIL="${EMAIL:-admin@ritualworks.com}"  # Email for Let's Encrypt notifications
KEYSTORE_PASSWORD="${KEYSTORE_PASS:-changeit}"

# Function to log messages
log_message() {
    local message=$1
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message" | tee -a $LOG_FILE
}

set_permissions() {
    local file=$1
    if [ -f "$file" ]; then
        chmod 644 "$file"
        echo "$(date '+%Y-%m-%d %H:%M:%S') - Set read permissions for $file" | tee -a $LOG_FILE
    else
        echo "$(date '+%Y-%m-%d %H:%M:%S') - File $file does not exist" | tee -a $LOG_FILE
    fi
}

# Function to generate self-signed certificates
generate_self_signed_cert() {
    local service=$1
    log_message "Generating self-signed certificate for $service..."
    mkdir -p $CERT_DIR
    if openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout $CERT_DIR/$service.key -out $CERT_DIR/$service.crt -subj "/CN=$service/O=Self-Signed Certificate"; then
        log_message "Self-signed certificate for $service generated successfully."
        chmod 644 "$CERT_DIR/$service.key" "$CERT_DIR/$service.crt"
        log_message "Set read permissions for $CERT_DIR/$service.key and $CERT_DIR/$service.crt"
    else
        log_message "Error generating self-signed certificate for $service."
    fi
}

# Function to obtain Let's Encrypt certificates
generate_lets_encrypt_cert() {
    local service=$1
    log_message "Generating Let's Encrypt certificate for $service..."
    if apt-get update && apt-get install -y certbot; then
        if certbot certonly --standalone --non-interactive --agree-tos --email $EMAIL -d $service --cert-name $service; then
            cp /etc/letsencrypt/live/$service/fullchain.pem $CERT_DIR/$service.crt
            cp /etc/letsencrypt/live/$service/privkey.pem $CERT_DIR/$service.key
            chmod 644 "$CERT_DIR/$service.key" "$CERT_DIR/$service.crt"
            log_message "Let's Encrypt certificate for $service generated and copied successfully."
            log_message "Set read permissions for $CERT_DIR/$service.key and $CERT_DIR/$service.crt"
        else
            log_message "Error generating Let's Encrypt certificate for $service."
        fi
    else
        log_message "Error installing certbot."
    fi
}

# Function to generate PKCS12 keystore
generate_p12_keystore() {
    local service=$1
    log_message "Generating PKCS12 keystore for $service..."

    if [ ! -f "$CERT_DIR/$service.crt" ] || [ ! -f "$CERT_DIR/$service.key" ]; then
        log_message "Certificate or key file missing for $service. Cannot generate PKCS12 keystore."
        return 1
    fi

    log_message "Running openssl pkcs12 -export -in $CERT_DIR/$service.crt -inkey $CERT_DIR/$service.key -out $CERT_DIR/$service.p12 -name $service -CAfile $CERT_DIR/$service.crt -caname root -password pass:$KEYSTORE_PASSWORD"
    if openssl pkcs12 -export \
        -in "$CERT_DIR/$service.crt" \
        -inkey "$CERT_DIR/$service.key" \
        -out "$CERT_DIR/$service.p12" \
        -name "$service" \
        -CAfile "$CERT_DIR/$service.crt" \
        -caname "root" \
        -password pass:$KEYSTORE_PASSWORD; then
        log_message "PKCS12 keystore for $service generated successfully."
        chmod 644 "$CERT_DIR/$service.p12"
        log_message "Set read permissions for $CERT_DIR/$service.p12"
    else
        log_message "Error generating PKCS12 keystore for $service."
        log_message "Error details: $(openssl pkcs12 -export -in "$CERT_DIR/$service.crt" -inkey "$CERT_DIR/$service.key" -out "$CERT_DIR/$service.p12" -name "$service" -CAfile "$CERT_DIR/$service.crt" -caname "root" -password pass:$KEYSTORE_PASSWORD 2>&1)"
        return 1
    fi
}

# Main script logic
services=("postgres"  "redis" "rabbitmq")
for service in "${services[@]}"; do
    if [ "$ENVIRONMENT" == "production" ]; then
        generate_lets_encrypt_cert $service
    else
        generate_self_signed_cert $service
    fi
    
    if [ "$service" == "elasticsearch" ]; then
        generate_p12_keystore $service
    fi
done

log_message "Certificate generation and permissions update completed."

# Display the certificate details
log_message "Generated certificates:"
ls -l $CERT_DIR | tee -a $LOG_FILE
