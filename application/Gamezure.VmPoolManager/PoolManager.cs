using System;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using IPVersion = Azure.ResourceManager.Network.Models.IPVersion;
using NetworkInterface = Azure.ResourceManager.Network.Models.NetworkInterface;
using NetworkManagementClient = Azure.ResourceManager.Network.NetworkManagementClient;
using NetworkProfile = Azure.ResourceManager.Compute.Models.NetworkProfile;

namespace Gamezure.VmPoolManager
{
    public class PoolManager
    {
        private readonly string subscriptionId;
        private readonly TokenCredential credential;
        private readonly IAzure azure;
        private readonly ResourcesManagementClient resourceClient;
        private readonly ComputeManagementClient computeClient;
        private readonly NetworkManagementClient networkManagementClient;
        private readonly ResourceGroupsOperations resourceGroupsClient;
        private readonly VirtualMachinesOperations virtualMachinesClient;
        private readonly VirtualNetworksOperations virtualNetworksClient;

        public PoolManager(string subscriptionId, TokenCredential credential, IAzure azure)
        {
            this.subscriptionId = subscriptionId;
            this.credential = credential;
            this.azure = azure;

            resourceClient = new ResourcesManagementClient(this.subscriptionId, this.credential);
            computeClient = new ComputeManagementClient(this.subscriptionId, this.credential);
            networkManagementClient = new NetworkManagementClient(this.subscriptionId, this.credential);
            
            resourceGroupsClient = resourceClient.ResourceGroups;
            virtualMachinesClient = computeClient.VirtualMachines;
            virtualNetworksClient = networkManagementClient.VirtualNetworks;
        }

        public PoolManager(string subscriptionId, IAzure azure) : this(subscriptionId, new DefaultAzureCredential(), azure)
        {
        }
        
        public async Task<VirtualMachine> CreateVm(VmCreateParams vmCreateParams)
        {
            VirtualNetwork vnet = await EnsureVnet(vmCreateParams.ResourceGroupName, vmCreateParams.ResourceLocation, vmCreateParams.VnetName);

            var ipAddress = await CreatePublicIpAddressAsync(vmCreateParams.ResourceGroupName, vmCreateParams.ResourceLocation, vmCreateParams.Name);
            var nic = await CreateNetworkInterfaceAsync(vmCreateParams.ResourceGroupName, vmCreateParams.ResourceLocation, vmCreateParams.Name, vnet.Subnets.First().Id, ipAddress.Id);
            VirtualMachine vm = await CreateWindowsVmAsync(vmCreateParams, nic.Id); 

            return vm;
        }

        public async Task<bool> GuardResourceGroup(string name)
        {
            bool exists = false;
            Response rgExists = await resourceGroupsClient.CheckExistenceAsync(name);

            if (rgExists.Status == 204) // 204 - No Content
            {
                exists = true;
            }

            return exists;
        }

        public async Task<VirtualNetwork> EnsureVnet(string resourceGroupName, string location, string vnetName)
        {
            VirtualNetwork vnet;
            var vnetList = this.virtualNetworksClient.List(resourceGroupName).ToList();
            bool vnetExists = vnetList.Exists(network => network.Name.Equals(vnetName));

            if (!vnetExists)
            {
                vnet = await CreateVirtualNetwork(vnetName, this.virtualNetworksClient, resourceGroupName, location);
                if (vnet is null)
                {
                    throw new Exception($"Could not create vnet {vnetName} in resource group {resourceGroupName}");
                }
            }
            var vnetResponse = await this.virtualNetworksClient.GetAsync(resourceGroupName, vnetName);

            return vnetResponse.Value;
        }

        public INetwork CreateVnet(string rgName, string location, string prefix, INetworkSecurityGroup nsgPublic, INetworkSecurityGroup nsgGame)
        {
            var network = azure.Networks.Define($"{prefix}-vnet")
                .WithRegion(location)
                .WithExistingResourceGroup(rgName)
                .WithAddressSpace("10.0.0.0/24")
                .DefineSubnet("public")
                    .WithAddressPrefix("10.0.0.0/27")
                    .WithExistingNetworkSecurityGroup(nsgPublic)
                    .Attach()
                .DefineSubnet("game")
                    .WithAddressPrefix("10.0.0.32/27")
                    .WithExistingNetworkSecurityGroup(nsgGame)
                    .Attach()
                .Create();
            
            return network;
        }

        public INetworkSecurityGroup CreateNetworkSecurityGroup(string rgName, string location, string prefix)
        {
            var name = $"{prefix}-nsg";
            
            int port = 25565;
            var networkSecurityGroup = azure.NetworkSecurityGroups.Define(name)
                .WithRegion(location)
                .WithExistingResourceGroup(rgName)
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

        public async Task<ResourceGroup> CreateResourceGroup(string resourceGroupName, string region)
        {
            var resourceGroupResponse = await resourceGroupsClient.CreateOrUpdateAsync(resourceGroupName, new ResourceGroup(region));
            return resourceGroupResponse.Value;
        }

        private static async Task<VirtualNetwork> CreateVirtualNetwork(string vnetName,
            VirtualNetworksOperations virtualNetworksClient, string resourceGroupName, string location)
        {
            var vnet = new VirtualNetwork
            {
                Location = location,
                AddressSpace = new AddressSpace()
                {
                    AddressPrefixes = { "10.0.0.0/16" }
                }
            };
            vnet.Subnets.Add(new Subnet
            {
                Name = resourceGroupName + "-subnet",
                AddressPrefix = "10.0.0.0/24",
            });

            await virtualNetworksClient.StartCreateOrUpdateAsync(resourceGroupName, vnetName, vnet);
            return vnet;
        }

        public async Task<VirtualMachine> CreateWindowsVmAsync(VmCreateParams vmCreateParams, string nicId)
        {
            // Create Windows VM

            var windowsVM = new VirtualMachine(vmCreateParams.ResourceLocation)
            {
                OsProfile = new OSProfile
                {
                    ComputerName = vmCreateParams.Name,
                    AdminUsername = vmCreateParams.UserName,
                    AdminPassword = vmCreateParams.UserPassword,
                },
                NetworkProfile = new NetworkProfile(),
                StorageProfile = new StorageProfile
                {
                    ImageReference = new ImageReference
                    {
                        Offer = "WindowsServer",
                        Publisher = "MicrosoftWindowsServer",
                        Sku = "2019-Datacenter",
                        Version = "latest"
                    },
                    // DataDisks = new List<DataDisk>()
                },
                HardwareProfile = new HardwareProfile { VmSize = VirtualMachineSizeTypes.StandardD3V2 },
            };
            
            
            windowsVM.NetworkProfile.NetworkInterfaces.Add(new NetworkInterfaceReference { Id = nicId });

            windowsVM = await (await this.virtualMachinesClient
                .StartCreateOrUpdateAsync(vmCreateParams.ResourceGroupName, vmCreateParams.Name, windowsVM)).WaitForCompletionAsync();

            return windowsVM;
        }

        public async Task<NetworkInterface> CreateNetworkInterfaceAsync(string rgName, string location, string namePrefix, string subnetId, string ipAddressId)
        {
            // Create Network interface
            var networkInterfaceIpConfiguration = new NetworkInterfaceIPConfiguration
            {
                Name = "Primary",
                Primary = true,
                Subnet = new Subnet { Id = subnetId },
                PrivateIPAllocationMethod = IPAllocationMethod.Dynamic,
                PublicIPAddress = new PublicIPAddress { Id = ipAddressId }
            };
            
            var nic = new NetworkInterface()
            {
                Location = location
            };
            nic.IpConfigurations.Add(networkInterfaceIpConfiguration);
            
            nic = await this.networkManagementClient.NetworkInterfaces
                .StartCreateOrUpdate(rgName, namePrefix + "_nic", nic)
                .WaitForCompletionAsync();
            
            return nic;
        }

        public async Task<PublicIPAddress> CreatePublicIpAddressAsync(string rgName, string location, string namePrefix)
        {
            // Create IP Address
            var ipAddress = new PublicIPAddress()
            {
                PublicIPAddressVersion = IPVersion.IPv4,
                PublicIPAllocationMethod = IPAllocationMethod.Dynamic,
                Location = location,
            };


            ipAddress = await this.networkManagementClient.PublicIPAddresses
                .StartCreateOrUpdate(rgName, namePrefix + "_ip", ipAddress)
                .WaitForCompletionAsync();

            return ipAddress;
        }

        public readonly struct VmCreateParams
        {
            public string Name { get; }
            public string UserName { get; }
            public string UserPassword { get; }
            public string VnetName { get; }
            public string ResourceGroupName { get; }
            public string ResourceLocation { get; }


            public VmCreateParams(string name, string userName, string userPassword, string vnetName, string resourceGroupName, string resourceLocation)
            {
                this.Name = name;
                this.UserName = userName;
                this.UserPassword = userPassword;
                this.VnetName = vnetName;
                this.ResourceGroupName = resourceGroupName;
                this.ResourceLocation = resourceLocation;
            }
        }
    }
}