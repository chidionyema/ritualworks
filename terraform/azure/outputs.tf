output "aks_cluster_id" {
  description = "The ID of the AKS cluster"
  value       = azurerm_kubernetes_cluster.aks.id
}

output "aks_cluster_fqdn" {
  description = "The FQDN of the AKS cluster"
  value       = azurerm_kubernetes_cluster.aks.fqdn
}

output "key_vault_id" {
  description = "The ID of the Key Vault"
  value       = azurerm_key_vault.kv.id
}

output "storage_account_id" {
  description = "The ID of the storage account"
  value       = azurerm_storage_account.storage_account.id
}

output "dns_zone_id" {
  description = "The ID of the DNS zone"
  value       = azurerm_dns_zone.dns_zone.id
}

output "a_record_fqdn" {
  description = "The fully qualified domain name of the A record"
  value       = azurerm_dns_a_record.a_record.fqdn
}

output "cname_record_fqdn" {
  description = "The fully qualified domain name of the CNAME record"
  value       = [for record in azurerm_dns_cname_record.cname_record : record.fqdn]
}
