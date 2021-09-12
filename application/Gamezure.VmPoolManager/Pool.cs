using System.Collections.Generic;
using System.Text.Json;
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

        public string VnetName { get; set; }

        public List<Vm> Vms { get; private set; } = new List<Vm>();

        public void InitializeVmList()
        {

            for (int i = 0; i < this.DesiredVmCount; i++)
            {
                var vm = new Vm
                {
                    Name = $"{this.Id}-vm-{i}",
                    PoolId = this.Id
                };
                this.Vms.Add(vm);
            }
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}