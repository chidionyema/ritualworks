resource "kubernetes_deployment" "minio" {
  metadata {
    name      = "minio"
    namespace = var.namespace
    labels = {
      app = "minio"
    }
  }

  spec {
    replicas = 1

    selector {
      match_labels = {
        app = "minio"
      }
    }

    template {
      metadata {
        labels = {
          app = "minio"
        }
      }

      spec {
        container {
          name  = "minio"
          image = "minio/minio:latest"

          args = ["server", "/data"]

          env = [
            {
              name = "MINIO_ACCESS_KEY"
              value_from = {
                secret_key_ref = {
                  name = kubernetes_secret.minio.metadata[0].name
                  key  = "MINIO_ACCESS_KEY"
                }
              }
            },
            {
              name = "MINIO_SECRET_KEY"
              value_from = {
                secret_key_ref = {
                  name = kubernetes_secret.minio.metadata[0].name
                  key  = "MINIO_SECRET_KEY"
                }
              }
            }
          ]

          ports {
            container_port = 9000
          }

          volume_mount {
            name       = "minio-storage"
            mount_path = "/data"
          }
        }

        volume {
          name = "minio-storage"

          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim.minio_pvc.metadata[0].name
          }
        }
      }
    }
  }
}
