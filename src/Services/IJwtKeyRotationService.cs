using System.Threading.Tasks;

namespace haworks.Services
{
    public interface IJwtKeyRotationService
    {
        Task RotateKeysIfNeededAsync();
    }
}