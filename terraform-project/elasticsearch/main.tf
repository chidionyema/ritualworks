# elasticsearch/main.tf

resource "kubernetes_stateful_set" "elasticsearch" {
  metadata {
    name      = "elasticsearch"
    namespace = var.namespace
    labels = {
      app = "elasticsearch"
    }
  }

  spec {
    service_name = "elasticsearch"

    replicas = 1

    selector {
      match_labels = {
        app = "elasticsearch"
      }
    }

    template {
      metadata {
        labels = {
          app = "elasticsearch"
        }
      }

      spec {
        container {
          name  = "elasticsearch"
          image = "docker.elastic.co/elasticsearch/elasticsearch:8.4.3"

     env = [
    {
      name  = "cluster.name"
      value = "my-cluster"
    },
    {
      name  = "node.name"
      value = "es-node-1"
    },
    {
      name  = "discovery.type"
      value = "single-node"
    },
    {
      name = "ELASTIC_PASSWORD"
      value_from = {  # Add '=' here
        secret_key_ref = {
          name = kubernetes_secret.elasticsearch.metadata[0].name
          key  = "ELASTIC_PASSWORD"
        }
      }
    }
  ]

          ports {
            container_port = 9200
          }

          volume_mount {
            name       = "elasticsearch-storage"
            mount_path = "/usr/share/elasticsearch/data"
          }
        }

        volume {
          name = "elasticsearch-storage"

          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim.elasticsearch_pvc.metadata[0].name
          }
        }
      }
    }
  }
}

resource "kubernetes_persistent_volume_claim" "elasticsearch_pvc" {
  metadata {
    name      = "elasticsearch-pvc"
    namespace = var.namespace
  }

  spec {
    access_modes = ["ReadWriteOnce"]

    resources {
      requests = {
        storage = "20Gi"
      }
    }
  }
}

resource "kubernetes_service" "elasticsearch" {
  metadata {
    name      = "elasticsearch"
    namespace = var.namespace
  }

  spec {
    selector = {
      app = "elasticsearch"
    }

    port {
      port        = 9200
      target_port = 9200
    }

    type = "ClusterIP"
  }
}
