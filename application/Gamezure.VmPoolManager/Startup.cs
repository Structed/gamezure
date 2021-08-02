using System;
using Gamezure.VmPoolManager.Repository;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(Gamezure.VmPoolManager.Startup))]

namespace Gamezure.VmPoolManager
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // builder.Services.AddHttpClient();

            string connectionString = Environment.GetEnvironmentVariable("CosmosDb");
            builder.Services.AddSingleton(s => new PoolRepository(connectionString));

            string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
            builder.Services.AddSingleton<PoolManager>(s => new PoolManager(subscriptionId));
        }
    }
}