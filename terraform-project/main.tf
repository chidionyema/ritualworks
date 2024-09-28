terraform {
  required_version = ">= 1.0.0"

  required_providers {
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 2.11.0"
    }
    helm = {
      source  = "hashicorp/helm"
      version = "~> 2.5.1"
    }
    null = {
      source  = "hashicorp/null"
      version = "~> 3.1.0"
    }
    local = {
      source  = "hashicorp/local"
      version = "~> 2.1.0"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 3.1.0"
    }
  }
}

# Providers
provider "kubernetes" {
  config_path    = var.local_testing ? "~/.kube/config" : "${path.module}/kubeconfig"
  config_context = var.local_testing ? var.kube_context : null
}

provider "helm" {
  kubernetes {
    config_path    = var.local_testing ? "~/.kube/config" : "${path.module}/kubeconfig"
    config_context = var.local_testing ? var.kube_context : null
  }
}
variable "kube_context" {
  description = "Kubernetes context to use when testing locally"
  type        = string
  default     = "docker-desktop"  # You can change this to the appropriate context for your local environment
}

# Generate SSH key pair
resource "tls_private_key" "ssh_key" {
  algorithm = "RSA"
  rsa_bits  = 4096
}

# Kubernetes Namespace
resource "kubernetes_namespace" "prod" {
  count = var.local_testing ? 0 : 1  # Only create when not local_testing

  metadata {
    name = var.namespace
  }

  depends_on = [null_resource.get_kubeconfig]  # Always depend on this
}

# Include Modules

module "postgres" {
  source            = "./postgres"
  namespace         = var.namespace
  postgres_user     = var.postgres_user
  postgres_password = var.postgres_password
  postgres_db       = var.postgres_db

  depends_on = [kubernetes_namespace.prod]
}



module "app1" {
  source                 = "./apps/app1/"
  namespace              = var.namespace
  aspnetcore_environment = var.aspnetcore_environment
  postgres_user          = var.postgres_user
  postgres_password      = var.postgres_password
  postgres_db            = var.postgres_db

  depends_on = [module.db_migrations]
}


module "redis" {
  source         = "./redis"
  namespace      = var.namespace
  redis_password = var.redis_password

  depends_on = [kubernetes_namespace.prod]
}

module "elasticsearch" {
  source                 = "./elasticsearch"
  namespace              = var.namespace
  elasticsearch_password = var.elasticsearch_password

  depends_on = [kubernetes_namespace.prod]
}

module "rabbitmq" {
  source            = "./rabbitmq"
  namespace         = var.namespace
  rabbitmq_user     = var.rabbitmq_user
  rabbitmq_password = var.rabbitmq_password

  depends_on = [kubernetes_namespace.prod]
}

module "minio" {
  source           = "./minio"
  namespace        = var.namespace
  minio_access_key = var.minio_access_key
  minio_secret_key = var.minio_secret_key

  depends_on = [kubernetes_namespace.prod]
}

module "vault" {
  source    = "./vault"
  namespace = var.namespace

  depends_on = [kubernetes_namespace.prod]
}

module "consul" {
  source    = "./consul"
  namespace = var.namespace

  depends_on = [kubernetes_namespace.prod]
}

module "haproxy" {
  source    = "./haproxy"
  namespace = var.namespace

}

module "db_migrations" {
  source                  = "./db_migrations"
  namespace               = var.namespace
  postgres_user           = var.postgres_user
  postgres_password       = var.postgres_password
  postgres_db             = var.postgres_db
  aspnetcore_environment  = var.aspnetcore_environment

  depends_on = [module.postgres, module.haproxy]
}





module "ingress" {
  source    = "./ingress"
  namespace = var.namespace

  depends_on = [module.app1, module.app2, module.app3]
}

module "monitoring" {
  source    = "./monitoring"
  namespace = var.namespace

  depends_on = [kubernetes_namespace.prod]

}
