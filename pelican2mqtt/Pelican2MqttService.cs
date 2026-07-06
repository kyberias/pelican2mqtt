using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace pelican2mqtt;

class Pelican2MqttService : BackgroundService
{
    private readonly Pelican2Mqtt programLogic;
    private readonly IHostApplicationLifetime lifetime;

    public Pelican2MqttService(Pelican2Mqtt programLogic, IHostApplicationLifetime lifetime)
    {
        this.programLogic = programLogic;
        this.lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await programLogic.Run(stoppingToken);
        lifetime.StopApplication();
    }
}