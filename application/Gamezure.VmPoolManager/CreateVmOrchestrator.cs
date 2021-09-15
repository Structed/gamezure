﻿using System.Collections.Generic;
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

            // Determine VMs present
            
            // Create per new VM:
            //  Create PIP
            //  Create Windows VM
            
            return outputs;
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
        
        [FunctionName("CreateVmOrchestrator_CreateWindowsVm")]
        public async Task<VirtualMachine> CreateWindowsVm([ActivityTrigger] IDurableActivityContext inputs, ILogger log)
        {
            (string nicId, PoolManager.VmCreateParams vmCreateParams) = inputs.GetInput<(string, PoolManager.VmCreateParams)>();
            log.LogInformation($"Creating Virtual Machine");

            return await poolManager.CreateWindowsVmAsync(vmCreateParams, nicId);
        }
    }
}