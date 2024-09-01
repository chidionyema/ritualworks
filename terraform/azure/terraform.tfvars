resource_group_name        = "myResourceGroup"
location                   = "eastus"
cluster_name               = "myAKSCluster"
azure_ad_admin_group_object_id = "<Your-Azure-AD-Admin-Group-Object-ID>"
key_vault_name             = "myKeyVault"
tenant_id                  = "<Your-Tenant-ID>"
azure_storage_account_name = "mystorageaccount"
domain_name                = "example.com"
ip_address                 = "192.168.1.1"
domain_validation_options = [
  {
    domain_name            = "example.com"
    resource_record_name   = "_abc123.example.com"
    resource_record_value  = "validation-token"
  }
]
