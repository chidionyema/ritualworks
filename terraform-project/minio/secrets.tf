# minio/secrets.tf

resource "kubernetes_secret" "minio" {
  metadata {
    name      = "minio-secret"
    namespace = var.namespace
  }

  data = {
    MINIO_ACCESS_KEY = base64encode(var.minio_access_key)
    MINIO_SECRET_KEY = base64encode(var.minio_secret_key)
  }

  type = "Opaque"
}
