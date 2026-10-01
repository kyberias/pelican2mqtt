using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;

class MqttPublisher
{
    private readonly IMqttFactory factory;
    private readonly IConfiguration config;
    private readonly ILogger<MqttPublisher> log;
    private readonly ApplicationConfig appConfig;
    private readonly string mqttTopicRoot;
    private readonly string deviceSerialNumber;
    private readonly bool autoDiscoveryEnabled;

    private readonly ConcurrentDictionary<string, byte[]> pending =
        new ConcurrentDictionary<string, byte[]>();

    private readonly SemaphoreSlim pendingSignal = new(0, 1);

    public MqttPublisher(
        IMqttFactory factory,
        IConfiguration config,
        ILogger<MqttPublisher> log,
        ApplicationConfig appConfig)
    {
        this.factory = factory;
        this.config = config;
        this.log = log;
        this.appConfig = appConfig;

        mqttTopicRoot = config["mqtt:baseTopic"];
        deviceSerialNumber = config["deviceSerialNumber"];
        autoDiscoveryEnabled = bool.Parse(config["homeAssistantAutoDiscovery"]);
    }

    public async Task Publish(CancellationToken cancel)
    {
        foreach (var reg in appConfig.ToMqtt)
        {
            reg.ValueChanged += Reg_ValueChanged;
        }

        try
        {
            bool autoConfigPerformed = false;

            var login = config["mqtt:username"];
            var password = config["mqtt:password"];
            var server = config["mqtt:broker"];

            var mqttOptions = new MqttClientOptionsBuilder()
#if DEBUG
                .WithClientId("PelicanDebug" + deviceSerialNumber)
#else
                .WithClientId("Pelican" + deviceSerialNumber)
#endif
                .WithTcpServer(server)
                .WithCredentials(login, password)
                .Build();

            while (!cancel.IsCancellationRequested)
            {
                using var client = factory.CreateMqttClient();

                while (!cancel.IsCancellationRequested)
                {
                    try
                    {
                        var res = await client.ConnectAsync(mqttOptions, cancel);

                        log.LogInformation(
                            "MQTT connect result {ResultCode}",
                            res.ResultCode);

                        if (res.ResultCode ==
                            MqttClientConnectResultCode.Success)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancel.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(
                            ex,
                            "MQTT connection failed, retrying");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cancel);
                }

                if (!autoConfigPerformed && autoDiscoveryEnabled)
                {
                    await AutoDiscovery(client, cancel);
                    autoConfigPerformed = true;
                }

                while (client.IsConnected &&
                       !cancel.IsCancellationRequested)
                {
                    if (pending.IsEmpty)
                    {
                        await pendingSignal.WaitAsync(cancel);
                    }

                    foreach (var item in pending.ToArray())
                    {
                        if (!client.IsConnected)
                        {
                            break;
                        }

                        try
                        {
                            log.LogDebug(
                                "Publishing to topic {Topic}",
                                item.Key);

                            await client.PublishAsync(
                                new MqttApplicationMessage
                                {
                                    Topic = item.Key,
                                    Payload = item.Value,
                                    Retain = true
                                },
                                cancel);

                            ((ICollection<KeyValuePair<string, byte[]>>)pending)
                                .Remove(item);
                        }
                        catch (OperationCanceledException)
                            when (cancel.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            log.LogWarning(
                                ex,
                                "MQTT publish failed for {Topic}",
                                item.Key);

                            break;
                        }
                    }
                }

                if (!cancel.IsCancellationRequested)
                {
                    log.LogWarning(
                        "MQTT connection lost, reconnecting");

                    await Task.Delay(TimeSpan.FromSeconds(1), cancel);
                }
            }
        }
        finally
        {
            foreach (var reg in appConfig.ToMqtt)
            {
                reg.ValueChanged -= Reg_ValueChanged;
            }
        }
    }

    private void Reg_ValueChanged(object sender, EventArgs e)
    {
        var r = (IMqttRegister)sender;

        log.LogDebug(
            "MQTT register {Topic} value changed: {Value}",
            r.Topic,
            r.Value);

        if (r.Value == null || r.Topic == null)
        {
            return;
        }

        var path = mqttTopicRoot + "/" + r.Topic;
        var payload = Encoding.UTF8.GetBytes(r.Value);

        pending[path] = payload;

        if (pendingSignal.CurrentCount == 0)
        {
            pendingSignal.Release();
        }
    }
