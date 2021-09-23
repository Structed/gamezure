using System.Collections.Generic;

namespace Gamezure.VmPoolManager
{
    public readonly struct VmCreateParams
    {
        public string Name { get; }
        public string PoolId { get; }
        public string UserName { get; }
        public string UserPassword { get; }
        public string ResourceGroupName { get; }
        public string ResourceLocation { get; }
        public IDictionary<string, string> Tags { get; }
        public Pool.Networking Networking { get; }


        public VmCreateParams(
            string name,
            string poolId,
            string userName,
            string userPassword,
            string resourceGroupName,
            string resourceLocation,
            IDictionary<string, string> tags,
            Pool.Networking networking)
        {
            this.Name = name;
            this.PoolId = poolId;
            this.UserName = userName;
            this.UserPassword = userPassword;
            this.Networking = networking;
            this.ResourceGroupName = resourceGroupName;
            this.ResourceLocation = resourceLocation;
            this.Tags = tags;
        }
    }
}