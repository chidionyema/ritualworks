# ingress/ingress.tf

variable "namespace" {
  type = string
}

# Define the Helm repository
resource "helm_repository" "ingress_nginx" {
  name = "ingress-nginx"
  url  = "https://kubernetes.github.io/ingress-nginx"
}

resource "helm_release" "nginx_ingress" {
  name       = "nginx-ingress"
  namespace  = var.namespace
  repository = helm_repository.ingress_nginx.name
  chart      = "ingress-nginx"
  version    = "4.0.19"

  values = [
    file("${path.module}/values.yaml")
  ]
}
