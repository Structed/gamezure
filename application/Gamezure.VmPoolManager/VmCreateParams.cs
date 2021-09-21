namespace Gamezure.VmPoolManager
{
    public readonly struct VmCreateParams
    {
        public string Name { get; }
        public string UserName { get; }
        public string UserPassword { get; }
        public string ResourceGroupName { get; }
        public string ResourceLocation { get; }
        public Pool.Networking Networking { get; }


        public VmCreateParams(string name, string userName, string userPassword, string resourceGroupName, string resourceLocation, Pool.Networking networking)
        {
            this.Name = name;
            this.UserName = userName;
            this.UserPassword = userPassword;
            this.Networking = networking;
            this.ResourceGroupName = resourceGroupName;
            this.ResourceLocation = resourceLocation;
        }
    }
}