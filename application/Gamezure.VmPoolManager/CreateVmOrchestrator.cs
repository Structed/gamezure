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
    public static class CreateVmOrchestrator
    {
        [FunctionName("CreateVmOrchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // Replace "hello" with the name of your Durable Activity Function.
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>("CreateVmOrchestrator_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]

            string poolId = "";
            await context.CallActivityAsync<string>("CreateVmOrchestrator_GetPool", poolId, "");
            
            return outputs;
        }

        [FunctionName("CreateVmOrchestrator_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
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
        public static async Task<Pool> GetPool([ActivityTrigger] string poolId, PoolRepository poolRepository, ILogger log)
        {
            log.LogInformation($"fetching Pool' ({poolId}) data");

            return await poolRepository.Get(poolId);
        }
        
        [FunctionName("CreateVmOrchestrator_CreatePublicIp")]
        public static async Task<PublicIPAddress> CreatePublicIp([ActivityTrigger] PoolManager.VmCreateParams vmCreateParams, PoolManager poolManager, ILogger log)
        {
            log.LogInformation("Creating Public IP Address");

            return await poolManager.CreatePublicIpAddressAsync(
                vmCreateParams.ResourceGroupName,
                vmCreateParams.ResourceLocation,
                vmCreateParams.Name);
        }
        
        [FunctionName("CreateVmOrchestrator_CreateNetworkInterface")]
        public static async Task<NetworkInterface> CreateNetworkInterface([ActivityTrigger] string subnetId, string ipAddressId, PoolManager.VmCreateParams vmCreateParams, PoolManager poolManager, ILogger log)
        {
            log.LogInformation($"Creating Network Interface with IP ID {ipAddressId} on subnet ID {subnetId}");

            return await poolManager.CreateNetworkInterfaceAsync(
                vmCreateParams.ResourceGroupName,
                vmCreateParams.ResourceLocation,
                vmCreateParams.Name,
                subnetId,
                ipAddressId);
        }
        
        [FunctionName("CreateVmOrchestrator_CreateWindowsVm")]
        public static async Task<VirtualMachine> CreateWindowsVm([ActivityTrigger] string nicId, PoolManager.VmCreateParams vmCreateParams, PoolManager poolManager, ILogger log)
        {
            log.LogInformation($"Creating Virtual Machine");

            return await poolManager.CreateWindowsVmAsync(vmCreateParams, nicId);
        }
    }
}