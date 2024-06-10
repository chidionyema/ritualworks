using System.Threading.Tasks;
using RitualWorks.Controllers;

namespace RitualWorks.Contracts
{
    public interface ISocialMediaService
    {
        Task PostRitualAsync(RitualDto ritualDto);
    }
}
