﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using ComputeManagementClient = Azure.ResourceManager.Compute.ComputeManagementClient;
using IPVersion = Azure.ResourceManager.Network.Models.IPVersion;
using NetworkInterface = Azure.ResourceManager.Network.Models.NetworkInterface;
using NetworkManagementClient = Azure.ResourceManager.Network.NetworkManagementClient;
using NetworkProfile = Azure.ResourceManager.Compute.Models.NetworkProfile;

namespace Gamezure.VmPoolManager
{
    public class PoolManager
    {
        private const string SUBNET_NAME_PUBLIC = "public";
        private const string SUBNET_NAME_GAME = "game";
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
        
        public async Task<IVirtualMachine> CreateVm(VmCreateParams vmCreateParams)
        {
            var tasks = new List<Task>(3);
            
            var taskVirtualNetwork = this.azure.Networks.GetByIdAsync(vmCreateParams.Networking.Vnet.Id);
            var taskNsgPublic = this.azure.NetworkSecurityGroups.GetByIdAsync(vmCreateParams.Networking.NsgPublic.Id);
            var taskNsgGame = this.azure.NetworkSecurityGroups.GetByIdAsync(vmCreateParams.Networking.NsgGame.Id);
            
            tasks.Add(taskVirtualNetwork);
            tasks.Add(taskNsgPublic);
            tasks.Add(taskNsgGame);
            Task.WaitAll(tasks.ToArray());

            var vmTasks = new List<Task>(2);
            var publicNic = this.FluentCreatePublicNetworkConnection(
                vmCreateParams.Name,
                taskVirtualNetwork.Result,
                taskNsgPublic.Result);
            
            var gameNic = this.FluentCreateGameNetworkConnection(
                vmCreateParams.Name,
                taskVirtualNetwork.Result,
                taskNsgGame.Result);
            
            Task.WaitAll(vmTasks.ToArray());

            var vm = await FluentCreateWindowsVm(vmCreateParams, publicNic.Result, gameNic.Result);

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

        public INetwork FluentCreateVnet(string rgName, string location, string prefix, INetworkSecurityGroup nsgPublic, INetworkSecurityGroup nsgGame)
        {
            var network = azure.Networks.Define($"{prefix}-vnet")
                .WithRegion(location)
                .WithExistingResourceGroup(rgName)
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

        public INetworkSecurityGroup FluentCreateNetworkSecurityGroup(string rgName, string location, string prefix)
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

        public async Task<IVirtualMachine> FluentCreateWindowsVm(VmCreateParams vmCreateParams, INetworkInterface nicPublic, INetworkInterface nicGame, CancellationToken cancellationToken = default)
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
                .CreateAsync(cancellationToken);

            return vm;
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

        /// <summary>
        /// Creates a new NIC in the <see cref="SUBNET_NAME_PUBLIC"/> subnet of the specified <paramref name="network"/>
        /// </summary>
        /// <param name="vmName">The VMs name - used as DNS name for the public IP</param>
        /// <param name="network">A network that should be used for Public Internet traffic</param>
        /// <param name="networkSecurityGroup">A security group attached to the Network and the Public Subnet</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The NIC</returns>
        public async Task<INetworkInterface> FluentCreatePublicNetworkConnection(
            string vmName,
            INetwork network,
            INetworkSecurityGroup networkSecurityGroup,
            CancellationToken cancellationToken = default)
        {
            var publicVnetName = network.Name;
            string subnetName = network.Subnets[SUBNET_NAME_PUBLIC].Name;

            INetworkInterface networkInterface = await this.azure.NetworkInterfaces.Define($"{vmName}-public-nic")
                .WithRegion(network.Region)
                .WithExistingResourceGroup(networkSecurityGroup.ResourceGroupName)
                .WithExistingPrimaryNetwork(network)
                .WithSubnet(subnetName)
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                .WithNewPrimaryPublicIPAddress(vmName)
                .CreateAsync(cancellationToken);

            return networkInterface;
        }

        /// <summary>
        /// Creates a new NIC in the <see cref="SUBNET_NAME_GAME"/> subnet of the specified <paramref name="network"/>
        /// </summary>
        /// <param name="network">A network that should be used for Public Internet traffic</param>
        /// <param name="networkSecurityGroup">A security group attached to the Network and the Game Subnet</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The NIC</returns>
        public async Task<INetworkInterface> FluentCreateGameNetworkConnection(
            string vmName,
            INetwork network,
            INetworkSecurityGroup networkSecurityGroup,
            CancellationToken cancellationToken = default)
        {
            string subnetName = network.Subnets[SUBNET_NAME_GAME].Name;

            INetworkInterface networkInterface = await this.azure.NetworkInterfaces.Define($"{vmName}-public-nic")
                .WithRegion(network.Region)
                .WithExistingResourceGroup(networkSecurityGroup.ResourceGroupName)
                .WithExistingPrimaryNetwork(network)
                .WithSubnet(subnetName)
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                .CreateAsync(cancellationToken);

            return networkInterface;
        }

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
}