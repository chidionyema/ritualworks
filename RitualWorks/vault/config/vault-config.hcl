# Use Consul as the storage backend
storage "consul" {
  address = "http://consul:8500" # The address of the Consul agent
  path    = "vault/"             # Consul KV path where Vault data will be stored
}

ha_storage "consul" {
  address  = "http://consul:8500" 
  path    = "vault/"
}


# Listener configuration
listener "tcp" {
  address     = "0.0.0.0:8200"   # Vault will listen on all available network interfaces
  tls_disable = "true"           # Disabling TLS (use only in development)
}

# Vault API and cluster configuration
api_addr     = "http://vault:8200"   # The address clients will use to talk to Vault
cluster_addr = "https://vault:8201"  # The address Vault nodes will use to talk to each other


# Additional Vault settings
disable_mlock = true   # Disabling mlock (required in containerized environments)
ui             = true  # Enable the Vault UI
