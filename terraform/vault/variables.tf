variable "vault_token" {
  description = "Vault token for authentication"
  type        = string
}

variable "services" {
  description = "List of services for which to create roles and generate certificates"
  type        = list(string)
  default     = [
    "postgres_primary",
    "postgres_standby",
    "redis-master",
    "redis-replica",
    "elasticsearch-node-1",
    "elasticsearch-node-2",
    "rabbitmq-node1",
    "rabbitmq-node2",
    "minio1",
    "minio2",
    "haproxy",
    "app1",
    "app2",
    "app3",
    "nginx"
  ]
}

variable "cert_dir" {
  description = "Directory to save generated certificates"
  type        = string
  default     = "../../vault/agent/sink"
}

variable "compose_file" {
  description = "Path to the Docker Compose file"
  type        = string
  default     = "../docker/compose/docker-compose-backend.yml"
}
