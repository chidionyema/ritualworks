
namespace RitualWorks.Contracts
{
    public interface IConnectionStringProvider
    {
      
        string GetConnectionString();
        void UpdateConnectionString();
    }
}