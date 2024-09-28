variable "namespace" {
  description = "The Kubernetes namespace to deploy resources into"
  type        = string
  default     = "prod"
}

# PostgreSQL variables
variable "postgres_user" {
  type        = string
  description = "PostgreSQL username"
  default     = "myuser"
}

variable "postgres_password" {
  type        = string
  description = "PostgreSQL password"
}

variable "postgres_db" {
  type        = string
  description = "PostgreSQL database name"
  default     = "your_postgres_db"
}

# Redis variables
variable "redis_password" {
  type        = string
  description = "Redis password"
}

# RabbitMQ variables
variable "rabbitmq_user" {
  type        = string
  description = "RabbitMQ username"
  default     = "rabbit_user"
}

variable "rabbitmq_password" {
  type        = string
  description = "RabbitMQ password"
}

# MinIO variables
variable "minio_access_key" {
  type        = string
  description = "MinIO access key"
}

variable "minio_secret_key" {
  type        = string
  description = "MinIO secret key"
}

# Vault variables
variable "vault_root_token" {
  type        = string
  description = "Vault root token"
}

variable "vault_token" {
  type        = string
  description = "Vault token"
}

# Application variables
variable "aspnetcore_environment" {
  type        = string
  description = "ASPNETCORE_ENVIRONMENT value"
  default     = "Production"
}

# Server IP and user variables
variable "server_ip" {
  description = "IP address of the Linux server"
  type        = string
}

variable "server_user" {
  description = "SSH username for the Linux server"
  type        = string
  default     = "root"
}

variable "local_testing" {
  description = "Flag to indicate if we're testing locally"
  type        = bool
  default     = false
}
