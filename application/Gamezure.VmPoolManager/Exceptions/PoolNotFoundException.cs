namespace Gamezure.VmPoolManager.Exceptions
{
    public class PoolNotFoundException : System.Exception
    {
        public string PoolId { get; }

        public PoolNotFoundException(string poolId)
        {
            this.PoolId = poolId;
        }
    }
}