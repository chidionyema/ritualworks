# haproxy/main.tf

variable "namespace" {
  type = string
}

provider "helm" {
  kubernetes {
    config_path = "~/.kube/config"
  }
}

# Define the Helm repository for HAProxy
resource "helm_repository" "haproxy" {
  name = "haproxy"
  url  = "https://haproxy.github.io/helm-charts"
}

# Use the Helm chart to deploy HAProxy
resource "helm_release" "haproxy" {
  name       = "haproxy"
  namespace  = var.namespace
  repository = helm_repository.haproxy.url
  chart      = "haproxy"
  version    = "1.13.1"  # Specify the chart version

  values = [
    file("${path.module}/values.yaml")
  ]

  set {
    name  = "NAMESPACE"
    value = var.namespace
  }
}
