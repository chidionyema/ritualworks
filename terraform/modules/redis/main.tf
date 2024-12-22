resource "helm_release" "redis" {
  name       = var.release_name
  namespace  = var.namespace
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "redis"
  version    = "17.9.3"  # pinned version

  # Master-Replica
  set {
    name  = "architecture"
    value = "replication"
  }

  # Credentials (disable or enable for dev)
  set {
    name  = "auth.enabled"
    value = false
  }

  # Persistence
  set {
    name  = "master.persistence.enabled"
    value = true
  }
  set {
    name  = "master.persistence.size"
    value = "1Gi"
  }
  set {
    name  = "replica.persistence.enabled"
    value = true
  }
  set {
    name  = "replica.persistence.size"
    value = "1Gi"
  }
}
