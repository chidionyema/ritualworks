# Docker Swarm with HashiCorp Vault Integration

This project sets up a Docker Swarm stack with integrated HashiCorp Vault for secrets management. The setup includes various services such as PostgreSQL, Redis, Elasticsearch, Prometheus, Grafana, and RabbitMQ.

## Directory Structure
Set the Encryption Passphrase:

Before running the script, export the ENCRYPTION_PASSPHRASE environment variable:

bash
Copy code
export ENCRYPTION_PASSPHRASE="your-strong-passphrase-here"
Security Note: Ensure the passphrase is strong and securely managed. Avoid hardcoding it or exposing it in logs, shell history, or process lists. You can prompt for it securely if necessary:

bash
Copy code
read -s -p "Enter encryption passphrase: " ENCRYPTION_PASSPHRASE
echo
Modify Necessary Variables:

Update placeholders like your_actual_password, your_postgres_db, and postgres_primary with your actual values.
Ensure that the paths like ../vault/config/role_id and ../vault/secrets/secret_id exist and are writable.
Ensure GPG is Installed:

Install GPG if it's not already installed:

bash
Copy code
# On Ubuntu/Debian
sudo apt-get install gnupg

# On CentOS/RHEL
sudo yum install gnupg
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


Vault for secrets management
HAProxy for load balancing and SSL termination
Redis with master-replica setup for caching
Elasticsearch cluster for search capabilities
RabbitMQ cluster for messaging
MinIO instances for object storage
Prometheus and Grafana for monitoring
PostgreSQL with primary-standby replication
Consul for service discovery and configuration
Custom Application Services with Nginx as a reverse proxy


How Components Work Together for Scalability and Availability
HAProxy and Nginx Load Balancing:

Distributes incoming requests across multiple instances of services like PostgreSQL and the application.
Supports scaling out by adding more backend instances without changing the client configuration.
Database Replication and Failover:

PostgreSQL primary-standby setup allows for automatic failover.
Repmgr manages replication and promotes standby to primary if needed.
Caching Layer with Redis:

Offloads frequent read requests from the database.
Master-replica setup ensures high availability and scalability for read operations.
Search Capabilities with Elasticsearch:

Clustered setup allows for distributing indexing and search load.
Data is sharded and replicated, improving performance and ensuring data availability.
Asynchronous Processing with RabbitMQ:

Decouples services by handling background tasks and message queuing.
Clustered setup ensures messages are not lost and can be processed even if a node fails.
Object Storage with MinIO:

Provides scalable storage for unstructured data like files and images.
Can be scaled by adding more instances in a distributed mode.
Monitoring and Alerting:

Prometheus collects metrics from all services, allowing for proactive scaling decisions.
Grafana visualizes metrics, helping in identifying bottlenecks or failures.
Service Discovery with Consul:

Dynamically discovers and configures services, enabling them to find each other without hard-coded addresses.
Facilitates scaling by automatically updating service catalogs.
Secrets Management with Vault:

Centralizes credential management, reducing security risks.
Scales by handling dynamic secret generation for an increasing number of services.

### access  vault UI
http://127.0.0.1:8200/ui
