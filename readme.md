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

#### Add Prometheus Data Source:

1. Open Grafana in your browser (`http://localhost:3001`).
2. Log in with the default credentials (`admin / admin`).
3. Add a new data source and select Prometheus.
4. Set the URL to `http://prometheus:9090` and save the data source.

#### Import Dashboards:

You can import predefined dashboards for Postgres, Redis, Elasticsearch, RabbitMQ, etc.

1. Go to the Dashboard section in Grafana.
2. Import a new dashboard by entering the dashboard ID from the Grafana website (e.g., 11074 for PostgreSQL, 763 for Redis, etc.).

### Setting Up Permissions for Grafana Provisioning Files

To ensure that Grafana can access and read the provisioning files correctly, follow these steps to set the appropriate ownership and permissions:

1. **Set Ownership to the Grafana User**

   Replace `/path/to` with the actual paths where your provisioning files and dashboards are located. The Grafana service typically runs with the user ID `472`, so it's important to adjust the ownership accordingly:

   ```bash
   # Set ownership to the Grafana user (replace with the correct user if needed)
   sudo chown -R 472:472 /path/to/config/provisioning/datasources
   sudo chown -R 472:472 /path/to/config/provisioning/dashboards
   sudo chown -R 472:472 /path/to/dashboards

2. **Set Read and Execute Permissions for Directories and Read Permissions for Files
```bash
   sudo chmod -R 755 /path/to/config/provisioning/datasources
   sudo chmod -R 755 /path/to/config/provisioning/dashboards
   sudo chmod -R 755 /path/to/dashboards

This step ensures that the Grafana service has the correct permissions to execute the necessary files:
3. *Ensure That Files Have Read Permissions

To make sure all files within the directories are readable, execute the following commands:


```bash
sudo find /path/to/config/provisioning/datasources -type f -exec chmod 644 {} \;
sudo find /path/to/config/provisioning/dashboards -type f -exec chmod 644 {} \;
sudo find /path/to/dashboards -type f -exec chmod 644 {} \;

e.g 
```bash
      sudo chown -R 472:472 ./provisioning/datasources
      sudo chown -R 472:472 ./provisioning/dashboards
      sudo chown -R 472:472 ./dashboards
       # Set read and execute permissions for directories, and read permissions for files
      sudo chmod -R 755 ./provisioning/datasources
      sudo chmod -R 755 ./provisioning/dashboards
      sudo chmod -R 755 ./dashboards
      # Ensure that files have read permissions
      sudo find ./provisioning/datasources -type f -exec chmod 644 {} \;
      sudo find ./provisioning/dashboards -type f -exec chmod 644 {} \;
      sudo find ./dashboards -type f -exec chmod 644 {} \;

      

4. *Restart Grafana

After adjusting the permissions, restart Grafana to apply the changes:
```bash docker-compose restart grafana

5. Check Grafana's logs to ensure that all provisioning files are being loaded correctly:
```bash docker logs <grafana_container_name>


* Vault is already initialized
delete vault/data folder  stop and delete vault and consul containers



### access  vault UI
http://127.0.0.1:8200/ui
