# Regional failover

Failover moves traffic from a primary region to its paired region.
eastus is paired with westus for failover.
westeurope is paired with northeurope for failover.

# DNS TTL

After a regional failover the DNS TTL is 60 seconds.
Wait for that TTL before telling a customer the cutover is finished.

# Provision lock

Do not start failover while a provision lock is held.
The provision lock lasts 15 minutes.
