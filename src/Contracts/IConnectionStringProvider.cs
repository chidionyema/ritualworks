using System.Threading.Tasks;

namespace haworks.Contracts
{
    public interface IConnectionStringProvider
    {
        Task<int> GetLeaseDurationAsync();
        Task<string> GetConnectionStringAsync();
        Task UpdateConnectionStringAsync();
    }
}