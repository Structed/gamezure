using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Compute.Models;
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

            string vnetName = "gamezure-vmpool-vnet";
            int vmCount = pool.DesiredVmCount;
            List<VirtualMachine> vms = new List<VirtualMachine>(vmCount);
            List<Task<VirtualMachine>> tasks = new List<Task<VirtualMachine>>(vmCount);
            
            for (int i = 0; i < vmCount; i++)
            {
                var vmCreateParams = new PoolManager.VmCreateParams(
                    $"gamezure-vm-{i}",
                    "gamezure-user",
                    Guid.NewGuid().ToString(),
                    vnetName,
                    pool.ResourceGroupName,
                    pool.Location
                );
                
                var item = poolManager.CreateVm(vmCreateParams);
                tasks.Add(item);
            }

            foreach (Task<VirtualMachine> task in tasks)
            {
                vms.Add(await task);
            }

            return new OkObjectResult(vms);
        }
    }
}