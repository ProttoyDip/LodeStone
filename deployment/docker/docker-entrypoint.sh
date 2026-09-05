#!/bin/sh
set -eu

certificate_path="${Kestrel__Certificates__Default__Path:-/https/lodestone.crt}"
private_key_path="${Kestrel__Certificates__Default__KeyPath:-/https/lodestone.key}"

require_non_placeholder_secret() {
    value="$1"
    label="$2"
    case "$value" in
        ""|*replace-with-*|*change-me*|*changeme*|*example*)
            echo "$label must be set to a real local-demo secret; refusing to start." >&2
            exit 64
            ;;
    esac
}

require_non_placeholder_secret "${LODESTONE_ADMIN_PASSWORD:-}" "LODESTONE_ADMIN_PASSWORD"
require_non_placeholder_secret "${LODESTONE_SQL_SA_PASSWORD:-}" "LODESTONE_SQL_SA_PASSWORD"

regenerate_certificate=false
if [ ! -s "$certificate_path" ] || [ ! -s "$private_key_path" ]; then
    regenerate_certificate=true
elif ! openssl x509 -checkend 86400 -noout -in "$certificate_path" >/dev/null 2>&1; then
    echo "The local HTTPS certificate is expired or expires within 24 hours; regenerating it."
    regenerate_certificate=true
elif [ "$(openssl x509 -noout -modulus -in "$certificate_path" 2>/dev/null | openssl sha256)" \
       != "$(openssl rsa -noout -modulus -in "$private_key_path" 2>/dev/null | openssl sha256)" ]; then
    echo "The local HTTPS certificate does not match its private key; regenerating it."
    regenerate_certificate=true
fi

if [ "$regenerate_certificate" = true ]; then
    echo "Generating a local self-signed HTTPS certificate. Use trusted TLS termination outside local Compose."
    umask 077
    rm -f "$certificate_path" "$private_key_path"
    openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 30 \
        -keyout "$private_key_path" \
        -out "$certificate_path" \
        -subj "/CN=localhost" \
        -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
fi

chown -R app:app /app/keys /https
exec su app -s /bin/sh -c 'exec dotnet /app/Lodestone.Web.dll'
