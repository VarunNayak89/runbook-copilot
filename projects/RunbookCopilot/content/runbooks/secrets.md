# Secret storage

Northwind Cloud stores application secrets in Key Vault.
Apps read those secrets with a managed identity.
Do not copy an API key into pipeline YAML.

# Catalog key rotation

Rotate the catalog key every 90 days.
Record the rotation in the change ticket before the old key is disabled.
