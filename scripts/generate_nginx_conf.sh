#!/bin/sh

LOG_FILE="/var/log/nginx/generate_nginx_conf.log"
mkdir -p /var/log/nginx

log_message() {
    local message="$1"
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message" >> $LOG_FILE
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message"
}

log_message "Script started."

# Load environment variables from .env file if it exists
if [ -f /etc/nginx/.env ]; then
    log_message "Loading environment variables from .env file."
    set -o allexport
    source /etc/nginx/.env
    set +o allexport
else
    log_message ".env file not found. Skipping loading environment variables from .env file."
fi

# Define required environment variables
required_vars="WORKER_CONNECTIONS API_SERVER_NAME APP_SERVER_1 APP_SERVER_2 APP_SERVER_3 QTRADER_SERVER QTRADER_SERVER_NAME PROMETHEUS_SERVER_NAME GRAFANA_SERVER_NAME SSL_CERTIFICATE SSL_CERTIFICATE_KEY QTRADER_SSL_CERTIFICATE QTRADER_SSL_CERTIFICATE_KEY QTRADER_SSL_TRUSTED_CERTIFICATE CORS_ORIGINS"

# Check if all required environment variables are set
for var in $required_vars; do
    if [ -z "$(eval echo \$$var)" ]; then
        log_message "Error: Environment variable $var is not set."
        exit 1
    fi
done

# Log the environment variables
log_message "Environment variables:"
printenv >> $LOG_FILE

# Substitute environment variables in the Nginx configuration template
log_message "Substituting environment variables."
envsubst '$WORKER_CONNECTIONS $API_SERVER_NAME $APP_SERVER_1 $APP_SERVER_2 $APP_SERVER_3 $QTRADER_SERVER $QTRADER_SERVER_NAME $PROMETHEUS_SERVER_NAME $GRAFANA_SERVER_NAME $SSL_CERTIFICATE $SSL_CERTIFICATE_KEY $QTRADER_SSL_CERTIFICATE $QTRADER_SSL_CERTIFICATE_KEY $QTRADER_SSL_TRUSTED_CERTIFICATE $CORS_ORIGINS' < /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf

# Check if the nginx.conf file was created
if [ ! -f /etc/nginx/nginx.conf ]; then
    log_message "Failed to create /etc/nginx/nginx.conf"
    exit 1
fi

# Log the content of the generated configuration
log_message "Generated nginx.conf:"
cat /etc/nginx/nginx.conf >> $LOG_FILE

# Output the generated configuration to the console for immediate feedback
cat /etc/nginx/nginx.conf

# Test Nginx configuration
log_message "Testing Nginx configuration."
nginx -t >> $LOG_FILE 2>&1

if [ $? -ne 0 ]; then
    log_message "Nginx configuration test failed."
    exit 1
fi

# Start Nginx
log_message "Starting Nginx."
nginx -g 'daemon off;'
