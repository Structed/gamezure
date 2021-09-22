using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Gamezure.VmPoolManager.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Gamezure.VmPoolManager
{
    public class EnsureVMs
    {
        private readonly PoolRepository poolRepository;
        private readonly PoolManager poolManager;

        public EnsureVMs(PoolRepository poolRepository, PoolManager poolManager)
        {
            this.poolRepository = poolRepository;
            this.poolManager = poolManager;
        }
        
        [FunctionName("EnsureVMs")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = null)]
            HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string poolId = req.Query["poolid"];
            if (string.IsNullOrWhiteSpace(poolId))
            {
                return new BadRequestObjectResult("Please pass a `poolid` as query parameter");
            }

            Pool pool = null;
            try
            {
                ItemResponse<Pool> response = await poolRepository.Get(poolId);
                pool = response.Resource;
            }
            catch (CosmosException cosmosException) when (cosmosException.StatusCode == HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult($"Could not find VM Pool {poolId}");
            }

            try
            {
                bool resourceGroupExists = await poolManager.GuardResourceGroup(pool.ResourceGroupName);
                if (!resourceGroupExists)
                {
                    return new NotFoundObjectResult($"Resource group {pool.ResourceGroupName} was not found.");
                }
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.Status)
                {
                    case 403:   // 403 - Forbidden
                        return new UnauthorizedResult();
                    default:
                        return new ObjectResult(requestFailedException) { StatusCode = requestFailedException.Status};
                }
            }

            var vms = CreateVirtualMachines(pool, log);

            return new OkObjectResult(vms);
        }

        private List<Vm> CreateVirtualMachines(Pool pool, ILogger log = null)
        {
            int vmCount = pool.Vms.Count;
            var vms = new List<Vm>(vmCount);
            var tasks = new List<Task<Vm>>(vmCount);

            foreach (var vm in pool.Vms)
            {
                var vmCreateParams = new VmCreateParams(
                    vm.Name,
                    vm.PoolId,
                    "gamezure-user",
                    Guid.NewGuid().ToString(), // TODO: Move credentials to KeyVault
                    pool.ResourceGroupName,
                    pool.Location,
                    pool.Net
                );

                var item = poolManager.CreateVm(vmCreateParams);
                if (!(log is null))
                {
                    log.LogInformation($"Started creating vm {vmCreateParams.Name}");
                }

                tasks.Add(item);
            }

            Task.WaitAll(tasks.ToArray());

            foreach (var task in tasks)
            {
                vms.Add(task.Result);
            }

            return vms;
        }
    }
}