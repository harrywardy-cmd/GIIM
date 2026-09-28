# Infrastructure

Azure resources will be defined here as Bicep templates in Phase 1. Planned (Australia East, DR in Australia Southeast):

| Resource | Purpose |
|---|---|
| App Service (Linux) | `Giim.Api` + static UI |
| Azure Functions / Container Apps Jobs | `Giim.Workers` |
| Azure SQL Database | Main database (geo-replica for DR) |
| Service Bus namespace | Work queues; the on-prem agent pulls from here (outbound only) |
| Key Vault | SDP, Okta and Graph secrets |
| Application Insights + Log Analytics | Monitoring and alerts |
| Managed Identities | No stored credentials between Azure services |
| Private endpoints / VNet | SQL, Key Vault and Service Bus not exposed publicly |
