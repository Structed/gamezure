# Gamezure
Azure Game Streaming VM management

Gamezure is a serverless application/API, which allows you to define a Pool of VMs; with an additional API call, Gamezure will take care of having as many VMs as specified in that pool - see docs below.
The pool not only consists of the created VMs, but also of two networks, two NICs per VM and a network security group for each of those networks, the machines have an internal network (e.g. for gameservers) and an external interface, reachable from the public Internet.
The API is built using C# and Azure Functions, making use of the Durable Functions framework.
Data is stored in a Cosmos Db, which uses serverless billing to reduce the baseline cost, as there is not much happening on database side.




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
* [Application flow](./docs/flow.md)

## Routes
### Adding a pool
    {{BASE_URI}}/AddPool?code={{AZURE_FUNCTION_KEY}}

With a body consisting like the following example JSON:

````json
{
"id": "pool-2",
"resourceGroupName": "rg-pool1-test",
"location": "westeurope",
"desiredVmCount": 2
}
````


### Trigger pool VM check
    {{BASE_URI}}/CreateVmOrchestrator_HttpStart??code={{AZURE_FUNCTION_KEY}}&poolId={{poolId}}
