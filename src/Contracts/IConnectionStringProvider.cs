
namespace RitualWorks.Contracts
{
    public interface IConnectionStringProvider
    {
        int GetLeaseDuration();
        string GetConnectionString();
        void UpdateConnectionString();
    }
}