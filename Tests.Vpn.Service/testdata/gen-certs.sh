#!/bin/bash
set -euo pipefail

# Generate a regular code signing certificate without the EV policy OID.
openssl req \
  -x509 \
  -newkey rsa:2048 \
  -keyout /dev/null \
  -out self-signed.crt \
  -days 3650 \
  -nodes \
  -subj "/CN=Coder Self Signed" \
  -addext "keyUsage=digitalSignature" \
  -addext "extendedKeyUsage=codeSigning"

# Generate an EV code signing certificate by adding the EV policy OID. We add
# a different OID before the EV OID to ensure the validator can handle multiple
# policies.
config="
[req]
distinguished_name = req_distinguished_name
x509_extensions = v3_req
prompt = no

[req_distinguished_name]
CN = Coder Self Signed EV

[v3_req]
keyUsage = digitalSignature
extendedKeyUsage = codeSigning
certificatePolicies = @pol1,@pol2

[pol1]
policyIdentifier = 2.23.140.1.4.1
CPS.1="https://coder.com"

[pol2]
policyIdentifier = 2.23.140.1.3
CPS.1="https://coder.com"
"

openssl req \
  -x509 \
  -newkey rsa:2048 \
  -keyout /dev/null \
  -out self-signed-ev.crt \
  -days 3650 \
  -nodes \
  -config <(echo "$config")
