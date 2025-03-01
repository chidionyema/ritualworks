#!/bin/bash
set -e  # exit immediately on error

if [ -f "/certs-volume/ca.crt" ]; then
  echo "Found CA certificate, updating trusted store..."
  cp "/certs-volume/ca.crt" /usr/local/share/ca-certificates/ca.crt
  update-ca-certificates
fi

dotnet ef database update --context OrderContext
dotnet ef database update --context ProductContext
dotnet ef database update --context ContentContext
dotnet ef database update --context IdentityContext


