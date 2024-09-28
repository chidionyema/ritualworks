# db_migrations/main.tf

variable "namespace" {
  type = string
}

variable "postgres_user" {
  type = string
}

variable "postgres_password" {
  type      = string
  sensitive = true
}

variable "postgres_db" {
  type = string
}

variable "aspnetcore_environment" {
  type = string
}

resource "kubernetes_secret" "db_migrations" {
  metadata {
    name      = "db-migrations-secret"
    namespace = var.namespace
  }

  data = {
    "ConnectionStrings__DefaultConnection" = base64encode("Host=haproxy.${var.namespace}.svc.cluster.local;Port=5432;Database=${var.postgres_db};Username=${var.postgres_user};Password=${var.postgres_password}")
    "ASPNETCORE_ENVIRONMENT"              = base64encode(var.aspnetcore_environment)
  }

  type = "Opaque"
}

resource "kubernetes_job" "db_migrations" {
  metadata {
    name      = "db-migrations"
    namespace = var.namespace
  }
   depends_on = [
    module.postgres,  # Ensure PostgreSQL is deployed
    module.haproxy    # Ensure HAProxy is deployed
  ]
  spec {
    backoff_limit = 4

    template {
      metadata {
        labels = {
          app = "db-migrations"
        }
      }

      spec {
        restart_policy = "OnFailure"

        container {
          name  = "db-migrations"
          image = "your_docker_image_with_migrations"  # Replace with your actual image

          command = ["dotnet", "ef", "database", "update"]

          env_from {
            secret_ref {
              name = kubernetes_secret.db_migrations.metadata[0].name
            }
          }

          resources {
            requests = {
              cpu    = "100m"
              memory = "128Mi"
            }
            limits = {
              cpu    = "200m"
              memory = "256Mi"
            }
          }
        }
      }
    }
  }
}
