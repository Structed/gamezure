using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Gamezure.VmPoolManager.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Gamezure.VmPoolManager
{
    public class AddPool
    {
        private readonly PoolRepository poolRepository;
        private readonly VmRepository vmRepository;
        private readonly PoolManager poolManager;

        public AddPool(PoolRepository poolRepository, VmRepository vmRepository, PoolManager poolManager)
        {
            this.poolRepository = poolRepository;
            this.vmRepository = vmRepository;
            this.poolManager = poolManager;
        }
        
        [FunctionName("AddPool")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = null)]
            HttpRequest req, ILogger log)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                Pool pool = JsonConvert.DeserializeObject<Pool>(requestBody);
                pool.ResourceGroupName = "gamezure-vmpool-rg";
                pool.Location = "westeurope";

                try
                {
                    _ = await this.poolRepository.Get(pool.Id);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                }
                catch (CosmosException e)
                {
                    return new ObjectResult(e) {StatusCode = (int)e.StatusCode};
                }

                var tags = new Dictionary<string, string>
                {
                    { "gamezure-pool-id", pool.Id }
                };
                var nsgPublic = poolManager.FluentCreateNetworkSecurityGroup(pool.ResourceGroupName, pool.Location, pool.Id + "-public", tags);
                var nsgGame = poolManager.FluentCreateNetworkSecurityGroup(pool.ResourceGroupName, pool.Location, pool.Id + "-game", tags);
                var network = poolManager.FluentCreateVnet(pool.ResourceGroupName, pool.Location, pool.Id, nsgPublic, nsgGame, tags);
                pool.Net = new Pool.Networking
                {
                    Vnet = new Pool.Networking.VirtualNetwork(network.Id, network.Name),
                    NsgPublic = new Pool.Networking.NetworkSecurityGroup(nsgPublic.Id, nsgPublic.Name),
                    NsgGame = new Pool.Networking.NetworkSecurityGroup(nsgGame.Id, nsgGame.Name)
                };
                
                try
                {
                    ItemResponse<Pool> response = await poolRepository.Save(pool);
                    return new OkObjectResult(response.Resource);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.Conflict)
                {
                    return new ConflictResult();
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.BadRequest)
                {
                    return new BadRequestObjectResult(e.ResponseBody);
                }
                catch (CosmosException e)
                {
                    return new ObjectResult(e) {StatusCode = StatusCodes.Status500InternalServerError};
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }
    }
}