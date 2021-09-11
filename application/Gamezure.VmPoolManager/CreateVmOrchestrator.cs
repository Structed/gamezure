using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network.Models;
using Gamezure.VmPoolManager.Parameters;
using Gamezure.VmPoolManager.Repository;
using Gamezure.VmPoolManager.Responses;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Gamezure.VmPoolManager
{
    public class CreateVmOrchestrator
    {
        private readonly PoolRepository poolRepository;
        private readonly PoolManager poolManager;

        public CreateVmOrchestrator(PoolRepository poolRepository, PoolManager poolManager)
        {
            this.poolRepository = poolRepository;
            this.poolManager = poolManager;
        }

        [FunctionName("CreateVmOrchestrator")]
        public async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();
            var poolId = context.GetInput<string>();
            Pool pool = await context.CallActivityAsync<Pool>("CreateVmOrchestrator_GetPool", poolId);
            outputs.Add(JsonConvert.SerializeObject(pool));
            
            // Check and potentially create:
            //  RG Exists & create
            var rgParams = new ResourceGroupParameters { Name = pool.ResourceGroupName, Location = pool.Location };
            var resourceGroupResponse = await context.CallActivityAsync<ResourceGroupResponse>("CreateVmOrchestrator_EnsureResourceGroup", rgParams);
            outputs.Add(JsonConvert.SerializeObject(resourceGroupResponse));

            
            //  Ensure VNet
            var vnetParams = new VnetParameters
            {
                Name = $"{pool.Id}-vnet",
                Location = resourceGroupResponse.Location,
                ResourceGroupName = resourceGroupResponse.Name
            };
            var vnetResponse = await context.CallActivityAsync<VnetResponse>("CreateVmOrchestrator_EnsureVnet", vnetParams);
            outputs.Add(JsonConvert.SerializeObject(vnetResponse));

            
            // Determine VMs present
            
            // Create per new VM:
            //  Create PIP
            //  Create Windows VM
            
            return outputs;
        }

        [FunctionName("CreateVmOrchestrator_Hello")]
        public string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("CreateVmOrchestrator_HttpStart")]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put")]
            HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            var poolId = req.RequestUri.ParseQueryString().Get("poolId");
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("CreateVmOrchestrator", null, poolId);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        
        [FunctionName("CreateVmOrchestrator_GetPool")]
        public async Task<Pool> GetPool([ActivityTrigger] string poolId, ILogger log)
        {
            log.LogInformation($"fetching Pool' ({poolId}) data");

            try
            {
                return await poolRepository.Get(poolId);
            }
            catch (CosmosException cosmosException)
            {
                switch (cosmosException.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        log.LogError($"Could not find Pool with ID {poolId}");
                        log.LogError(cosmosException, "Ex");
                        break;
                    default:
                        throw;
                }
                
            }

            return null;
        }

        [FunctionName("CreateVmOrchestrator_EnsureResourceGroup")]
        public async Task<ResourceGroupResponse> EnsureResourceGroup([ActivityTrigger] ResourceGroupParameters resourceGroupParameters, ILogger log)
        {
            var rg = await this.poolManager.CreateResourceGroup(resourceGroupParameters.Name, resourceGroupParameters.Location);
            return new ResourceGroupResponse
            {
                Id = rg.Id,
                Name = rg.Name,
                Location = rg.Location
            };
        }

        [FunctionName("CreateVmOrchestrator_EnsureVnet")]
        public async Task<VnetResponse> EnsureVnet([ActivityTrigger] VnetParameters vnetParameters, ILogger log)
        {
            var vnet = await this.poolManager.EnsureVnet(vnetParameters.ResourceGroupName, vnetParameters.Location, vnetParameters.Name);
            return new VnetResponse
            {
                Id = vnet.Id,
                Name = vnet.Name,
                Location = vnet.Location,
            };
        }
        
        [FunctionName("CreateVmOrchestrator_CreatePublicIp")]
        public async Task<PublicIPAddress> CreatePublicIp([ActivityTrigger] PoolManager.VmCreateParams vmCreateParams, ILogger log)
        {
            log.LogInformation("Creating Public IP Address");

            return await this.poolManager.CreatePublicIpAddressAsync(
                vmCreateParams.ResourceGroupName,
                vmCreateParams.ResourceLocation,
                vmCreateParams.Name);
        }
        
        [FunctionName("CreateVmOrchestrator_CreateNetworkInterface")]
        public async Task<NetworkInterface> CreateNetworkInterface([ActivityTrigger] IDurableActivityContext inputs, ILogger log)
        {
            (string subnetId, string ipAddressId, PoolManager.VmCreateParams vmCreateParams) = inputs.GetInput<(string, string, PoolManager.VmCreateParams)>();
            log.LogInformation($"Creating Network Interface with IP ID {ipAddressId} on subnet ID {subnetId}");

            return await poolManager.CreateNetworkInterfaceAsync(
                vmCreateParams.ResourceGroupName,
                vmCreateParams.ResourceLocation,
                vmCreateParams.Name,
                subnetId,
                ipAddressId);
        }
        
        [FunctionName("CreateVmOrchestrator_CreateWindowsVm")]
        public async Task<VirtualMachine> CreateWindowsVm([ActivityTrigger] IDurableActivityContext inputs, ILogger log)
        {
            (string nicId, PoolManager.VmCreateParams vmCreateParams) = inputs.GetInput<(string, PoolManager.VmCreateParams)>();
            log.LogInformation($"Creating Virtual Machine");

            return await poolManager.CreateWindowsVmAsync(vmCreateParams, nicId);
        }
    }
}