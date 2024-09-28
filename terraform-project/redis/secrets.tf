# redis/secrets.tf

resource "kubernetes_secret" "redis" {
  metadata {
    name      = "redis-secret"
    namespace = var.namespace
  }

  data = {
    REDIS_PASSWORD = base64encode(var.redis_password)
  }

  type = "Opaque"
}
