#!/bin/bash
# Generate a secure 32-byte encryption key for Serf
# This generates a base64-encoded key suitable for AES-256-GCM encryption

KEY=$(openssl rand -base64 32)

echo "=================================================================="
echo "  NSerf Encryption Key Generator"
echo "=================================================================="
echo ""
echo "Generated 32-byte encryption key (AES-256-GCM):"
echo "$KEY"
echo ""
echo "Usage in docker-compose.yml:"
echo "  environment:"
echo "    - SERF_ENCRYPT_KEY=$KEY"
echo ""
echo "Usage in command line:"
echo "  export SERF_ENCRYPT_KEY='$KEY'"
echo ""
echo "=================================================================="
