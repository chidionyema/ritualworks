using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using MassTransit;
using System.Threading.Tasks;

public partial class Program
{
    public class MyConsumer : IConsumer<MyMessage>
    {
        public async Task Consume(ConsumeContext<MyMessage> context)
        {
            // Handle the message
            await Task.Run(() =>
            {
                // Your message handling logic
            });
        }
    }

}
