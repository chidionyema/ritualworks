output "pki_urls" {
  value = vault_pki_secret_backend_config_urls.pki_urls
}

output "root_cert" {
  value = vault_pki_secret_backend_root_cert.root_cert.certificate
}

output "roles" {
  value = vault_pki_secret_backend_role.pki_roles
}
