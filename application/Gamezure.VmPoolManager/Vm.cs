using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gamezure.VmPoolManager
{
    public class Vm
    {
        /// <summary>
        /// Effectively the name of the VM
        /// </summary>
        // [JsonPropertyName("id")]
        public string Name { get; set; }
        public string PoolId { get; set; }
        public string ResourceId { get; set; } = string.Empty;
        public string PublicIp { get; set; } = string.Empty;
        public ProvisioningState State { get; private set;  } = ProvisioningState.None;

        /// <summary>
        /// Advance the state to the next possible state
        /// </summary>
        /// <returns>The new provisioning state after the transition</returns>
        public ProvisioningState NextProvisioningState()
        {
            var values = Enum.GetValues(typeof(ProvisioningState));
            var maxValue = values.GetUpperBound(0);
            var maxValueState = Enum.Parse<ProvisioningState>(maxValue.ToString());
            if (this.State == maxValueState)
            {
                this.State = ProvisioningState.None;
            }
            else
            {
                this.State++;
            }

            return this.State;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

        public enum ProvisioningState
        {
            None = 0,
            Creating = 1,
            Created = 2,
            Deleting = 3,
            Deleted = 4,
        }
    }
}