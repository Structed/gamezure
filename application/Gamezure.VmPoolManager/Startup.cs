using System;
using Gamezure.VmPoolManager.Repository;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Extensions.DependencyInjection;


[assembly: FunctionsStartup(typeof(Gamezure.VmPoolManager.Startup))]

namespace Gamezure.VmPoolManager
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // builder.Services.AddHttpClient();
            
            string tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            string clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            string clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
            string connectionString = Environment.GetEnvironmentVariable("CosmosDb");

            var credentials = new AzureCredentialsFactory().FromServicePrincipal(clientId, clientSecret, tenantId, AzureEnvironment.AzureGlobalCloud);
            var azure = Microsoft.Azure.Management.Fluent.Azure.Authenticate(credentials).WithSubscription(subscriptionId);
            
            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };
            var client = new CosmosClient(connectionString, clientOptions);
            builder.Services.AddSingleton<CosmosClient>(s => client);
            
            builder.Services.AddSingleton<IAzure>(s => azure);
            builder.Services.AddSingleton<PoolRepository>();
            builder.Services.AddSingleton<VmRepository>();
            builder.Services.AddSingleton<PoolManager>();
        }
    }
}