# Provision a tenant

Northwind Cloud creates a tenant from a catalog id, a tenant name, and a region.
Do not invent a region in this runbook. Look up the allowed list in the regions runbook before you choose.

# What provisioning does

Provisioning writes the tenant record, assigns a managed identity to the app, and holds a short name lock so two operators cannot create the same name at the same time.
Length of a temporary account is a different runbook.
Moving traffic to another region is a different runbook.

# Naming

Tenant names use lowercase letters and digits.
The name Contoso appears only as a worked example.
