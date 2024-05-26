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
        [JsonPropertyName("id")]
        public string Id { get; set; }
        public string PoolId { get; set; }
        public string ResourceGroupName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string PublicIp { get; set; } = string.Empty;
        public string PublicIpId { get; set; } = string.Empty;
        public string PublicNicId { get; set; } = string.Empty;
        public string GameNicId { get; set; } = string.Empty;
        public ProvisioningState State { get; private set;  } = ProvisioningState.None;
        public string Username { get; set; } = "gamezure";
        public string UserPass { get; set; }

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
        
        public bool SetProvisioningState(ProvisioningState state)
        {
            if (state == this.State + 1) {
                this.State = state;
                return true;
            } else {
                return false;
            }
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