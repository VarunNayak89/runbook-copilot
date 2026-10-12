# Trial duration

A trial tenant lasts 14 days.
The end date is stored in ExpiresOn as a UTC calendar date.
When that date arrives the tenant stops serving traffic.

# Trial storage

A trial tenant gets Standard_LRS storage redundancy.
Paid tenants are not described here.

# Leaving a trial

Clear ExpiresOn and switch the account to a paid catalog to keep the tenant.
A trial stays in the region where it was created.
