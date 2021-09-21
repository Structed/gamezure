﻿using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Gamezure.VmPoolManager.Repository;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Management.Compute.Fluent;
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

            var tasks = new List<Task>();
            foreach (var vm in pool.Vms)
            {
                var vmResultTask = VmResultTask(context, vm, pool, outputs);
                tasks.Add(vmResultTask);
            }

            await Task.WhenAll(tasks);
            
            return outputs;
        }

        private async Task<IVirtualMachine> VmResultTask(IDurableOrchestrationContext context, Vm vm, Pool pool, List<string> outputs)
        {
            var vmCreateParams = new PoolManager.VmCreateParams(vm.Name, "gamezure", "DzPY2uwGYxofahfD38CDrUjhc", pool.ResourceGroupName, pool.Location, pool.Net);
            var vmResultTask = await context.CallActivityAsync<IVirtualMachine>("CreateVmOrchestrator_CreateWindowsVm", vmCreateParams);
            outputs.Add($"Finished creation of {vmResultTask}");
            return vmResultTask;
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
        public async Task<IVirtualMachine> CreateWindowsVm([ActivityTrigger] PoolManager.VmCreateParams vmCreateParams, ILogger log)
        {
            log.LogInformation($"Creating Virtual Machine");
            
            return await poolManager.CreateVm(vmCreateParams);
        }
    }
}