# Docker Swarm with HashiCorp Vault Integration

This project sets up a Docker Swarm stack with integrated HashiCorp Vault for secrets management. The setup includes various services such as PostgreSQL, Redis, Elasticsearch, Prometheus, Grafana, and RabbitMQ.

## Directory Structure

```
your-project/
├── .env
├── consul-template/
│   ├── config/
│   │   └── config.hcl
│   └── templates/
│       └── env.ctmpl
├── docker/
│   ├── vault/
│   │   └── vault-compose.yml
│   ├── swarm/
│   │   └── docker-stack.yml
├── policy.hcl
├── scripts/
│   └── deploy_with_vault.sh
└── README.md
```

## Setup Instructions

### Prerequisites

- Docker and Docker Compose installed
- Docker Swarm initialized
- Access to the internet to pull Docker images

### Automated Deployment

1. **Configure Environment Variables:**

   Ensure you have a `.env` file in the root of your project directory with the following content:

   ```env
   ENVIRONMENT=development
   EMAIL=your-email@example.com
   POSTGRES_DB=your_db
   POSTGRES_USER=your_user
   POSTGRES_PASSWORD=your_password
   ELASTIC_PASSWORD=your_elastic_password
   STRIPE_API_KEY=your_stripe_api_key
   AWS_ACCESS_KEY_ID=your_aws_access_key
   AWS_SECRET_ACCESS_KEY=your_aws_secret_key
   AWS_DEFAULT_REGION=your_aws_region
   RABBITMQ_DEFAULT_USER=your_rabbitmq_user
   RABBITMQ_DEFAULT_PASS=your_rabbitmq_password
   ```

2. **Run the Deployment Script:**

   Execute the deployment script to deploy the entire stack:

   ```sh
   cd scripts
   ./deploy_with_vault.sh
   ```

### Notes

- Ensure all required Docker images and services are available and properly configured.
- This setup uses `VAULT_DEV_ROOT_TOKEN_ID` for development purposes. Use a secure method to handle Vault tokens and secrets in production.



### Configure Grafana
### Add Prometheus Data Source:

Open Grafana in your browser (http://localhost:3001).
Log in with the default credentials (admin / admin).
Add a new data source and select Prometheus.
Set the URL to http://prometheus:9090 and save the data source.
### Import Dashboards:

You can import predefined dashboards for Postgres, Redis, Elasticsearch, RabbitMQ, etc.
Go to the Dashboard section in Grafana and import a new dashboard by entering the dashboard ID from the Grafana website (e.g., 11074 for PostgreSQL, 763 for Redis, etc.).

### VAULT

to deploy
run ./scripts/deploy_vault.sh to deploy vault
save contents of scripts/unseal_keys.json externally and delete the file

run ./scripts/create_vault_token.sh to generate new root token to use for vault_tls_cert_generation.sh
run ./scripts/vault_tls_cert_generation.sh with new root token when prompted

Confirm that the Vault PKI and roles have been configured correctly by checking the roles directly in Vault using the following command
docker exec -e VAULT_ADDR=http://127.0.0.1:8200 -e VAULT_TOKEN=<your-token> compose-vault-1 vault list pki/roles


### access UI
http://127.0.0.1:8200/ui
