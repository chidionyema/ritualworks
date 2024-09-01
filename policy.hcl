# policy.hcl
path "secret/*" {
  capabilities = ["read"]
}
