using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
namespace RitualWorks.Services
{
    public class SocialMediaService : ISocialMediaService
    {
        private readonly IEnumerable<ISocialMediaService> _socialMediaServices;

        public SocialMediaService(IEnumerable<ISocialMediaService> socialMediaServices)
        {
            _socialMediaServices = socialMediaServices;
        }

        public async Task PostRitualAsync(RitualDto ritualDto)
        {
            var tasks = new List<Task>();

            foreach (var service in _socialMediaServices)
            {
                tasks.Add(service.PostRitualAsync(ritualDto));
            }

            await Task.WhenAll(tasks);
        }
    }
}
