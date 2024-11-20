
namespace haworks.Contracts
{
    public interface IConnectionStringProvider
    {
        int GetLeaseDuration();
        string GetConnectionString();
        void UpdateConnectionString();
    }
}