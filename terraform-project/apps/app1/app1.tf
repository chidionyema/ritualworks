# apps/app1.tf
variable "namespace" {
  description = "Namespace in Kubernetes"
  type        = string
}

variable "aspnetcore_environment" {
  description = "ASP.NET Core environment (e.g., Production, Development)"
  type        = string
}

variable "postgres_user" {
  description = "Postgres user"
  type        = string
}

variable "postgres_password" {
  description = "Postgres password"
  type        = string
}

variable "postgres_db" {
  description = "Postgres database name"
  type        = string
}

resource "kubernetes_deployment" "app1" {
  metadata {
    name      = "app1"
    namespace = var.namespace
    labels = {
      app = "app1"
    }
  }
 depends_on = [module.db_migrations]
  spec {
    replicas = 2

    selector {
      match_labels = {
        app = "app1"
      }
    }

    template {
      metadata {
        labels = {
          app = "app1"
        }
      }

      spec {
        container {
          name  = "app1"
          image = "your_docker_image:latest"

          env = [
            {
              name  = "ASPNETCORE_ENVIRONMENT"
              value = var.aspnetcore_environment
            },
            {
              name  = "ConnectionStrings__DefaultConnection"
              value = "Host=postgres.${var.namespace}.svc.cluster.local;Port=5432;Database=${var.postgres_db};Username=${var.postgres_user};Password=${var.postgres_password}"
            },
            # Add other environment variables
          ]

          ports {
            container_port = 8080
          }

          volume_mount {
            name       = "uploads"
            mount_path = "/app/uploads"
          }
        }

        volume {
          name = "uploads"

          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim.uploads_pvc.metadata[0].name
          }
        }
      }
    }
  }
}

resource "kubernetes_persistent_volume_claim" "uploads_pvc" {
  metadata {
    name      = "uploads-pvc"
    namespace = var.namespace
  }

  spec {
    access_modes = ["ReadWriteMany"]

    resources {
      requests = {
        storage = "5Gi"
      }
    }
  }
}

resource "kubernetes_service" "app1" {
  metadata {
    name      = "app1"
    namespace = var.namespace
  }

  spec {
    selector = {
      app = "app1"
    }

    port {
      port        = 80
      target_port = 8080
    }

    type = "ClusterIP"
  }
}
