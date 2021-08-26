using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network.Models;
using Gamezure.VmPoolManager.Repository;
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
        public async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // Replace "hello" with the name of your Durable Activity Function.
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]

            string poolId = "";
            // await context.CallActivityAsync<string>("CreateVmOrchestrator_GetPool", new {poolId, poolRepository, log});
            
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]
            HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("CreateVmOrchestrator", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        
        [FunctionName("CreateVmOrchestrator_GetPool")]
        public async Task<Pool> GetPool([ActivityTrigger] string poolId, ILogger log)
        {
            log.LogInformation($"fetching Pool' ({poolId}) data");

            return await poolRepository.Get(poolId);
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