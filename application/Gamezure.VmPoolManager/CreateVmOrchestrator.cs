using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

            try
            {
                var poolId = context.GetInput<string>();
                Pool pool = await context.CallActivityAsync<Pool>("CreateVmOrchestrator_GetPool", poolId);
                outputs.Add(JsonConvert.SerializeObject(pool));
                
                var tags = new Dictionary<string, string>
                {
                    { "gamezure-pool-id", pool.Id }
                };

                var tasks = new List<Task>();
                foreach (var vm in pool.Vms)
                {
                    var vmResultTask = VmResultTask(context, vm, pool, tags, outputs);
                    tasks.Add(vmResultTask);
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception e)
            {
                outputs.Add(e.Message);
            }

            return outputs;
        }

        private async Task<Vm> VmResultTask(IDurableOrchestrationContext context, Vm vm, Pool pool, IDictionary<string, string> tags, List<string> outputs)
        {
            var vmCreateParams = new VmCreateParams(vm.Name, vm.PoolId, "gamezure", vm.Password, pool.ResourceGroupName, pool.Location, tags, pool.Net);
            Vm vmResult = await context.CallActivityAsync<Vm>("CreateVmOrchestrator_CreateWindowsVm", vmCreateParams);
            outputs.Add($"Finished creation of {vmResult}");
            return vmResult;
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
        public async Task<Vm> CreateWindowsVm([ActivityTrigger] VmCreateParams vmCreateParams, ILogger log)
        {
            log.LogInformation($"Creating Virtual Machine");
            
            return await poolManager.CreateVm(vmCreateParams);
        }
        
        [FunctionName("CreateVmOrchestrator_GeneratePassword")]
        public string CreateWindowsVm([ActivityTrigger] IDurableActivityContext inputs, ILogger log)
        {
            log.LogInformation($"Generating Password");
            var password = Guid.NewGuid().ToString();
            log.LogInformation($"Password: {password}");

            return password;
        }
    }
}