variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "location" {
  description = "Azure region where the resources will be created"
  type        = string
}

variable "cluster_name" {
  description = "Name of the AKS cluster"
  type        = string
}

variable "azure_ad_admin_group_object_id" {
  description = "Azure AD Admin Group Object ID"
  type        = string
}

variable "key_vault_name" {
  description = "Name of the Key Vault"
  type        = string
}

variable "tenant_id" {
  description = "Tenant ID"
  type        = string
}

variable "azure_storage_account_name" {
  description = "Name of the storage account"
  type        = string
}

variable "domain_name" {
  description = "The domain name for the DNS zone"
  type        = string
}

variable "ip_address" {
  description = "The IP address for the A record"
  type        = string
}

variable "domain_validation_options" {
  description = "Domain validation options for ACM"
  type = list(object({
    domain_name            = string
    resource_record_name   = string
    resource_record_value  = string
  }))
  default = []
}
