using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.ResourceManager.Compute.Models;
using Gamezure.VmPoolManager.Repository;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;

namespace Gamezure.VmPoolManager
{
    public class PoolManager
    {
        private const string SUBNET_NAME_PUBLIC = "public";
        private const string SUBNET_NAME_GAME = "game";
        private readonly IAzure azure;
        private readonly VmRepository vmRepository;

        public PoolManager(IAzure azure, VmRepository vmRepository)
        {
            this.azure = azure;
            this.vmRepository = vmRepository;
        }
        
        public async Task<Vm> CreateVm(VmCreateParams vmCreateParams)
        {
            var tasks = new List<Task>(3);
            
            var taskVirtualNetwork = this.azure.Networks.GetByIdAsync(vmCreateParams.Networking.Vnet.Id);
            var taskNsgPublic = this.azure.NetworkSecurityGroups.GetByIdAsync(vmCreateParams.Networking.NsgPublic.Id);
            var taskNsgGame = this.azure.NetworkSecurityGroups.GetByIdAsync(vmCreateParams.Networking.NsgGame.Id);
            
            tasks.Add(taskVirtualNetwork);
            tasks.Add(taskNsgPublic);
            tasks.Add(taskNsgGame);
            Task.WaitAll(tasks.ToArray());

            var publicIp = await this.FluentCreatePublicIp(vmCreateParams);

            var vmTasks = new List<Task>(2);
            var taskNicPublic = this.FluentCreatePublicNetworkConnection(
                vmCreateParams.Name,
                taskVirtualNetwork.Result,
                taskNsgPublic.Result,
                publicIp,
                vmCreateParams.Tags);
            
            var taskNicGame = this.FluentCreateGameNetworkConnection(
                vmCreateParams.Name,
                taskVirtualNetwork.Result,
                taskNsgGame.Result,
                vmCreateParams.Tags);
            
            vmTasks.Add(taskNicPublic);
            vmTasks.Add(taskNicGame);
            
            Task.WaitAll(vmTasks.ToArray());

            INetworkInterface nicPublic = taskNicPublic.Result;
            var nicGame = taskNicGame.Result;
            
            var vm = await FluentCreateWindowsVm(vmCreateParams, nicPublic, nicGame);
            var vmResult = new Vm
            {
                Id = vm.Name,
                PoolId = vmCreateParams.PoolId,
                ResourceId = vm.Id,
                PublicIp = vm.GetPrimaryPublicIPAddress().IPAddress,    // same as publicIp.IPAddress
                PublicIpId = publicIp.Id,
                PublicNicId = nicPublic.Id,
                GameNicId = nicGame.Id,
                UserPass = vmCreateParams.UserPassword
            };

            await this.vmRepository.Save(vmResult);

            return vmResult;
        }
        
        public List<Vm> InitializeVmList(Pool pool, Func<string> passwordFunction)
        {
            var vms = new List<Vm>(pool.DesiredVmCount);
            for (int i = 0; i < pool.DesiredVmCount; i++)
            {
                var vm = new Vm
                {
                    Id = $"{pool.Id}-vm-{i}",
                    PoolId = pool.Id,
                    ResourceGroupName = pool.ResourceGroupName,
                    Location = pool.Location,
                    UserPass = passwordFunction(),
                };
                vm.NextProvisioningState();
                vms.Add(vm);
            }

            return vms;
        }

        public async Task<bool> GuardResourceGroup(string poolResourceGroupName)
        {
            return await this.azure.ResourceGroups.ContainAsync(poolResourceGroupName);
        }

        public INetwork FluentCreateVnet(
            string rgName,
            string location,
            string prefix,
            INetworkSecurityGroup nsgPublic,
            INetworkSecurityGroup nsgGame,
            IDictionary<string, string> tags)
        {
            var network = azure.Networks.Define($"{prefix}-vnet")
                .WithRegion(location)
                .WithExistingResourceGroup(rgName)
                .WithTags(tags)
                .WithAddressSpace("10.0.0.0/24")
                .DefineSubnet(SUBNET_NAME_PUBLIC)
                    .WithAddressPrefix("10.0.0.0/27")
                    .WithExistingNetworkSecurityGroup(nsgPublic)
                    .Attach()
                .DefineSubnet(SUBNET_NAME_GAME)
                    .WithAddressPrefix("10.0.0.32/27")
                    .WithExistingNetworkSecurityGroup(nsgGame)
                    .Attach()
                .Create();
            
            return network;
        }

        public INetworkSecurityGroup FluentCreateNetworkSecurityGroup(string rgName, string location, string prefix, IDictionary<string, string> tags)
        {
            var name = $"{prefix}-nsg";
            
            int port = 25565;
            var networkSecurityGroup = azure.NetworkSecurityGroups.Define(name)
                .WithRegion(location)
                .WithExistingResourceGroup(rgName)
                .WithTags(tags)
                .DefineRule("minecraft-tcp")
                .AllowInbound()
                .FromAnyAddress()
                .FromAnyPort()
                .ToAnyAddress()
                .ToPort(port)
                .WithProtocol(Microsoft.Azure.Management.Network.Fluent.Models.SecurityRuleProtocol.Tcp)
                .WithPriority(100)
                .WithDescription("Allow Minecraft TCP")
                .Attach()
                .DefineRule("minecraft-udp")
                .AllowInbound()
                .FromAnyAddress()
                .FromAnyPort()
                .ToAnyAddress()
                .ToPort(port)
                .WithProtocol(Microsoft.Azure.Management.Network.Fluent.Models.SecurityRuleProtocol.Udp)
                .WithPriority(101)
                .WithDescription("Allow Minecraft UDP")
                .Attach()
                .Create();
            
            return networkSecurityGroup;
        }

        public async Task<IVirtualMachine> FluentCreateWindowsVm(
            VmCreateParams vmCreateParams,
            INetworkInterface nicPublic,
            INetworkInterface nicGame,
            CancellationToken cancellationToken = default)
        {
            var imageReference = new ImageReference
            {
                Offer = "WindowsServer",
                Publisher = "MicrosoftWindowsServer",
                Sku = "2019-Datacenter",
                Version = "latest"
            };

            var vm = await this.azure.VirtualMachines.Define(vmCreateParams.Name)
                .WithRegion(vmCreateParams.ResourceLocation)
                .WithExistingResourceGroup(vmCreateParams.ResourceGroupName)
                .WithExistingPrimaryNetworkInterface(nicPublic)
                .WithLatestWindowsImage(imageReference.Publisher, imageReference.Offer, imageReference.Sku)
                .WithAdminUsername(vmCreateParams.UserName)
                .WithAdminPassword(vmCreateParams.UserPassword)
                .WithExistingSecondaryNetworkInterface(nicGame)
                .WithSize(Microsoft.Azure.Management.Compute.Fluent.Models.VirtualMachineSizeTypes.StandardD3V2)
                .WithTags(vmCreateParams.Tags)
                .CreateAsync(cancellationToken);

            return vm;
        }

        /// <summary>
        /// Creates a new NIC in the <see cref="SUBNET_NAME_PUBLIC"/> subnet of the specified <paramref name="network"/>
        /// </summary>
        /// <param name="vmName">The VMs name - used as DNS name for the public IP</param>
        /// <param name="network">A network that should be used for Public Internet traffic</param>
        /// <param name="networkSecurityGroup">A security group attached to the Network and the Public Subnet</param>
        /// <param name="publicIpAddress">The public IP address to use for teh VM. Should be a statically allocated IP</param>
        /// <param name="tags">Azure resource tags</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The NIC</returns>
        public async Task<INetworkInterface> FluentCreatePublicNetworkConnection(string vmName,
            INetwork network,
            INetworkSecurityGroup networkSecurityGroup,
            IPublicIPAddress publicIpAddress,
            IDictionary<string, string> tags,
            CancellationToken cancellationToken = default)
        {
            string subnetName = network.Subnets[SUBNET_NAME_PUBLIC].Name;

            INetworkInterface networkInterface = await this.azure.NetworkInterfaces.Define($"{vmName}-public-nic")
                .WithRegion(network.Region)
                .WithExistingResourceGroup(networkSecurityGroup.ResourceGroupName)
                .WithExistingPrimaryNetwork(network)
                .WithSubnet(subnetName)
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                .WithExistingPrimaryPublicIPAddress(publicIpAddress)
                .WithTags(tags)
                .CreateAsync(cancellationToken);

            return networkInterface;
        }

        public async Task<IPublicIPAddress> FluentCreatePublicIp(VmCreateParams vmCreateParams, CancellationToken cancellationToken = default)
        {
            var pip = await this.azure.PublicIPAddresses.Define($"{vmCreateParams.Name}-pip")
                .WithRegion(vmCreateParams.ResourceLocation)
                .WithExistingResourceGroup(vmCreateParams.ResourceGroupName)
                .WithTags(vmCreateParams.Tags)
                .WithLeafDomainLabel(vmCreateParams.Name)
                .WithStaticIP()
                .WithSku(PublicIPSkuType.Standard)
                .CreateAsync(cancellationToken);

            return pip;
        }

        /// <summary>
        /// Creates a new NIC in the <see cref="SUBNET_NAME_GAME"/> subnet of the specified <paramref name="network"/>
        /// </summary>
        /// <param name="vmName">The name of teh VM. Will be used to generate the NIC's name</param>
        /// <param name="network">A network that should be used for Public Internet traffic</param>
        /// <param name="networkSecurityGroup">A security group attached to the Network and the Game Subnet</param>
        /// <param name="tags">Azure resource tags</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The NIC</returns>
        public async Task<INetworkInterface> FluentCreateGameNetworkConnection(
            string vmName,
            INetwork network,
            INetworkSecurityGroup networkSecurityGroup,
            IDictionary<string, string> tags,
            CancellationToken cancellationToken = default)
        {
            string subnetName = network.Subnets[SUBNET_NAME_GAME].Name;

            INetworkInterface networkInterface = await this.azure.NetworkInterfaces.Define($"{vmName}-game-nic")
                .WithRegion(network.Region)
                .WithExistingResourceGroup(networkSecurityGroup.ResourceGroupName)
                .WithExistingPrimaryNetwork(network)
                .WithSubnet(subnetName)
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                .WithTags(tags)
                .CreateAsync(cancellationToken);

            return networkInterface;
        }
    }
}