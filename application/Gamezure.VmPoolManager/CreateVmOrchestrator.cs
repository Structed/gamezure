using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network.Models;
using Gamezure.VmPoolManager.Repository;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

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
        public async Task<List<Pool>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<Pool>();
            var poolId = context.GetInput<string>();
            outputs.Add(await context.CallActivityAsync<Pool>("CreateVmOrchestrator_GetPool", poolId));
            
            // EnsureVMs
            
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