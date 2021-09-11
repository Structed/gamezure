# Gamezure
Azure Game Streaming VM management

# Prerequisites
* An [Azure Subscription](https://azure.microsoft.com/en-us/solutions/gaming/)
* [An Azure Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
* [Terraform](https://terraform.io)
* [.NET Core 3.1](https://dot.net)

# Environment Variables
| Name     | Description    |
|----------|----------|
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID; `id` property when executing `az account show` |
| `AZURE_TENANT_ID` | Azure Tenant ID; `tenantId` property when executing `az account show` |
| `TF_VAR_sp_client_id` | ID of the Azure Service Principal who should have permissions to change the VM pool, `appId` in `az ad sp list --show-mine` |


# Setting up infrastructure in Azure
## Init Terraform:

    terraform init -backend-config='.\config\backend.local.config.tf'

## Apply
    
    terraform apply

# Development Prerequisites
* An [Azure Subscription](https://azure.microsoft.com/en-us/solutions/gaming/)
* [An Azure Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
* [.NET Core 3.1 SDK](https://dot.net)
* CosmosDB, either on Azure, or using the [CosmosDB Emulator](https://docs.microsoft.com/en-us/azure/cosmos-db/local-emulator?tabs=ssl-netstd21)
* Azure BlobStorage, or use [Azurite](https://github.com/Azure/Azurite) as a local storage emulator.

# Docs
*  [Application flow](./docs/flow.md)