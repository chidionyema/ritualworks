# ingress/ingress-resource.tf

resource "kubernetes_ingress" "apps_ingress" {
  metadata {
    name      = "apps-ingress"
    namespace = var.namespace
    annotations = {
      "kubernetes.io/ingress.class" = "nginx"
    }
  }

  spec {
    rule {
      http {
        path {
          path = "/app1"
          backend {
            service_name = module.app1.kubernetes_service.app1.metadata[0].name
            service_port = 80
          }
        }
        path {
          path = "/app2"
          backend {
            service_name = module.app2.kubernetes_service.app2.metadata[0].name
            service_port = 80
          }
        }
        path {
          path = "/app3"
          backend {
            service_name = module.app3.kubernetes_service.app3.metadata[0].name
            service_port = 80
          }
        }
      }
    }
  }
}
