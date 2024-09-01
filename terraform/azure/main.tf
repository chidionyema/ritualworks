provider "azurerm" {
  features {}
}

# Resource Group
resource "azurerm_resource_group" "aks_rg" {
  name     = var.resource_group_name
  location = var.location
}

# Azure Kubernetes Service (AKS) Cluster
resource "azurerm_kubernetes_cluster" "aks" {
  name                = var.cluster_name
  location            = azurerm_resource_group.aks_rg.location
  resource_group_name = azurerm_resource_group.aks_rg.name
  dns_prefix          = var.cluster_name

  default_node_pool {
    name       = "default"
    node_count = var.node_count  # Reduced node count
    vm_size    = var.vm_size     # Use a right-sized instance type

    # Enable Spot VMs for cost savings
    spot_max_price = var.spot_max_price  # Set maximum price for Spot VMs
    eviction_policy = "Delete"           # Delete VMs when evicted
    enable_auto_scaling = true           # Enable auto-scaling for cost efficiency
    min_count = 1
    max_count = 2
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin = "azure"
    network_policy = "azure"
  }

  lifecycle {
    ignore_changes = [
      default_node_pool[0].node_count  # Ignore changes to node count to avoid unnecessary updates
    ]
  }
}

# Key Vault
resource "azurerm_key_vault" "kv" {
  name                        = var.key_vault_name
  resource_group_name         = azurerm_resource_group.aks_rg.name
  location                    = azurerm_resource_group.aks_rg.location
  tenant_id                   = var.tenant_id
  sku_name                    = "standard"
  purge_protection_enabled    = true
  enabled_for_disk_encryption = true
}

# Key Vault Certificate
resource "azurerm_key_vault_certificate" "cert" {
  name         = "aks-cert"
  key_vault_id = azurerm_key_vault.kv.id

  certificate_policy {
    issuer_parameters {
      name = "Self"
    }

    key_properties {
      exportable = true
      key_size   = 2048
      key_type   = "RSA"
      reuse_key  = true
    }

    lifetime_action {
      action {
        action_type = "AutoRenew"
      }

      trigger {
        days_before_expiry = 30
      }
    }

    secret_properties {
      content_type = "application/x-pkcs12"
    }

    x509_certificate_properties {
      key_usage          = ["cRLSign", "dataEncipherment", "digitalSignature", "keyEncipherment", "keyAgreement"]
      subject            = "CN=${var.cluster_name}.${var.location}.cloudapp.azure.com"
      validity_in_months = 12
    }
  }
}

# Storage Account with Lifecycle Management
resource "azurerm_storage_account" "storage_account" {
  name                     = var.azure_storage_account_name
  resource_group_name      = azurerm_resource_group.aks_rg.name
  location                 = azurerm_resource_group.aks_rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  lifecycle_rule {
    name     = "lifecycle"
    enabled  = true

    rule {
      filters {
        blob_types = ["blockBlob"]
      }

      actions {
        base_blob {
          delete {
            days_after_modification_greater_than = 365
          }
          tier_to_archive {
            days_after_modification_greater_than = 30
          }
        }
      }
    }
  }
}

# Storage Container
resource "azurerm_storage_container" "storage_container" {
  name                  = "mycontainer"
  storage_account_name  = azurerm_storage_account.storage_account.name
  container_access_type = "private"
}

# Azure DNS Zone (Consider using a free DNS provider)
resource "azurerm_dns_zone" "dns_zone" {
  name                = var.domain_name
  resource_group_name = azurerm_resource_group.aks_rg.name
}

# DNS A Record for the AKS Cluster
resource "azurerm_dns_a_record" "a_record" {
  name                = "@"
  zone_name           = azurerm_dns_zone.dns_zone.name
  resource_group_name = azurerm_resource_group.aks_rg.name
  ttl                 = 300
  records             = [var.ip_address]
}

# Example of creating a CNAME record for certificate validation (if needed)
resource "azurerm_dns_cname_record" "cname_record" {
  for_each = {
    for dvo in var.domain_validation_options : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
    }
  }

  name                = each.value.name
  zone_name           = azurerm_dns_zone.dns_zone.name
  resource_group_name = azurerm_resource_group.aks_rg.name
  ttl                 = 300
  record              = each.value.record
}

variable "resource_group_name" {
  description = "The name of the resource group."
}

variable "location" {
  description = "The location of the resources."
}

variable "cluster_name" {
  description = "The name of the AKS cluster."
}

variable "key_vault_name" {
  description = "The name of the Key Vault."
}

variable "tenant_id" {
  description = "The tenant ID for the Azure subscription."
}

variable "azure_storage_account_name" {
  description = "The name of the Azure storage account."
}

variable "domain_name" {
  description = "The domain name for DNS."
}

variable "ip_address" {
  description = "The IP address for the AKS cluster."
}

variable "domain_validation_options" {
  description = "Domain validation options for DNS."
  type = list(object({
    domain_name             = string
    resource_record_name    = string
    resource_record_value   = string
  }))
}

variable "node_count" {
  description = "The number of nodes in the default node pool."
  default     = 1
}

variable "vm_size" {
  description = "The VM size for the default node pool."
  default     = "Standard_B2s"  # Smaller instance type for cost savings
}

variable "spot_max_price" {
  description = "Maximum price for Spot VMs."
  default     = "-1"  # Use the current on-demand price as the ceiling
}
