using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace Gamezure.VmPoolManager.Repository
{
    public class VmRepository
    {
        private readonly CosmosClient client;
        private readonly Container container;

        public VmRepository(CosmosClient client)
        {
            this.client = client;
            
            this.container = client.GetContainer("gamezure-db", "vm");
        }

        public Task<ItemResponse<Vm>> Save(Vm element)
        {
            return this.container.CreateItemAsync(element, new PartitionKey(element.PoolId));
        }

        private Task<ItemResponse<Vm>> UpsertItemAsync(Vm vm)
        {
            return this.container.UpsertItemAsync(vm, new PartitionKey(vm.PoolId));
        }

        public async Task<List<Vm>> UpsertMany(List<Vm> vms)
        {
            try
            {
                var tasks = new List<Task>(vms.Count);
                foreach (var vm in vms)
                {
                    var task = UpsertItemAsync(vm);
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception e)
            {
                // Console.WriteLine(e);
                throw;
            }
            return vms;
        }

        public Task<ItemResponse<Vm>> Get(string name)
        {
            return this.container.ReadItemAsync<Vm>(name, new PartitionKey(name));
        }

        public async Task<List<Vm>> GetAllByPoolId(string poolId)
        {
            QueryDefinition query = new QueryDefinition("select * from T where T.poolId = @poolId")
                .WithParameter("@poolId", poolId);
            
            var results = new List<Vm>();

            using (FeedIterator<Vm> resultSetIterator = container.GetItemQueryIterator<Vm>(
                query,
                requestOptions: new QueryRequestOptions()
                {
                    PartitionKey = new PartitionKey(poolId)
                }))
            {
                while (resultSetIterator.HasMoreResults)
                {
                    FeedResponse<Vm> response = await resultSetIterator.ReadNextAsync();
                    results.AddRange(response);
                    if (response.Diagnostics != null)
                    {
                        // Console.WriteLine($"\nQueryWithSqlParameters Diagnostics: {response.Diagnostics.ToString()}");
                    }
                }
            }

            return results;
        }
    }
}