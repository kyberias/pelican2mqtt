using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using pelican2mqtt.Mqtt;
using pelican2mqtt.Pelican;
using pelican2mqtt.Serial;

namespace pelican2mqtt;

class Pelican2Mqtt
{
    private readonly IConfiguration config;
    private readonly ILogger<Pelican2Mqtt> log;
    readonly SerialPortReader serialPortReader;
    private readonly EnerventMessageProcessor messageProcessor;
    private readonly MqttPublisher mqttPublisher;

    public Pelican2Mqtt(IConfiguration config, ILogger<Pelican2Mqtt> log, MqttPublisher mqttPublisher, EnerventMessageProcessor messageProcessor, SerialPortReader serialPortReader)
    {
        this.config = config;
        this.log = log;
        this.mqttPublisher = mqttPublisher;
        this.messageProcessor = messageProcessor;
        this.serialPortReader = serialPortReader;
    }

    public async Task Run(CancellationToken stoppingToken)
    {
        var options = new Options
        {
            SerialPort = config["serialPort"],
            MqttLogin = config["mqtt:username"],
            MqttPassword = config["mqtt:password"],
            MqttServer = config["mqtt:broker"]
        };

        if (string.IsNullOrEmpty(options.MqttServer) || string.IsNullOrEmpty(options.MqttLogin) ||
            string.IsNullOrEmpty(options.MqttPassword))
        {
            throw new Exception("MQTT settings required.");
        }

        log.LogInformation("Started");

        var byteBuffer = new BufferBlock<byte>();
        var messages = new BufferBlock<byte[]>();

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            var reader = serialPortReader.ReadBytesFromPort(byteBuffer, options.SerialPort, cancel.Token);
            var parser = EnerventProtocolParser.Parse(log, byteBuffer, messages, cancel.Token);
            var processor = messageProcessor.ProcessMessagesFromPelican(messages, cancel.Token);
            var publisher = mqttPublisher.Publish(cancel.Token);

            var tasks = new[] { reader, parser, processor, publisher };

            while (true)
            {
                await Task.WhenAny(tasks);

                if (publisher is { IsFaulted: true, IsCanceled: false })
                {
                    try
                    {
                        await publisher;
                    }
                    catch { }

                    publisher = mqttPublisher.Publish(cancel.Token);
                    tasks = new[] { reader, parser, processor, publisher };
                }
                else
                {
                    break;
                }
            }

            cancel.Cancel();

            foreach (var t in tasks)
            {
                try
                {
                    await t;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }
}