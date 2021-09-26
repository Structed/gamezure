﻿using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gamezure.VmPoolManager
{
    public class Pool
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        public string ResourceGroupName { get; set; }
        public string Location { get; set; }
        public int DesiredVmCount { get; set; }
        public Networking Net { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

        public class Networking
        {
            public VirtualNetwork Vnet { get; set; }
            public NetworkSecurityGroup NsgPublic { get; set; }
            public NetworkSecurityGroup NsgGame { get; set; }
            
            public class VirtualNetwork
            {
                public string Id { get; }
                public string Name { get; }

                public VirtualNetwork(string id, string name)
                {
                    this.Id = id;
                    this.Name = name;
                }
            }

            public class NetworkSecurityGroup
            {
                public string Id { get; }
                public string Name { get; }

                public NetworkSecurityGroup(string id, string name)
                {
                    this.Id = id;
                    this.Name = name;
                }
            }
        }
    }
}