using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using pelican2mqtt.Mqtt;
using pelican2mqtt.Pelican;
using pelican2mqtt.Serial;

namespace pelican2mqtt
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await CreateHostBuilder(args).Build().RunAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddHostedService<Pelican2MqttService>();
                    services.AddSingleton<Pelican2Mqtt>();
                    services.AddSingleton<SerialPortReader>();
                    services.AddSingleton<EnerventMessageProcessor>();
                    services.AddSingleton<MqttPublisher>();
                    services.AddSingleton<ApplicationConfig>();
                    services.AddSingleton<IMqttFactory, MqttFactory>();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddCommandLine(args);
                    config.AddEnvironmentVariables();
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                });
    }
}
