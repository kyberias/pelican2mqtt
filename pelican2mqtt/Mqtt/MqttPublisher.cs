using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace pelican2mqtt.Mqtt;

class MqttPublisher
{
    private readonly IMqttFactory factory;
    private readonly IConfiguration config;
    private readonly ILogger<MqttPublisher> log;
    private readonly ApplicationConfig appConfig;
    private readonly string mqttTopicRoot;
    private readonly string deviceSerialNumber;
    private readonly bool autoDiscoveryEnabled;

    // Only the latest unpublished value for each topic is kept.
    private readonly ConcurrentDictionary<string, byte[]> pending =
        new ConcurrentDictionary<string, byte[]>();

    // Used only to wake up the publisher when new data arrives.
    // Maximum count of one is enough, because pending contains the data.
    private readonly SemaphoreSlim pendingSignal = new SemaphoreSlim(0, 1);

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

                try
                {
                    //
                    // Connect / reconnect
                    //
                    while (!cancel.IsCancellationRequested)
                    {
                        try
                        {
                            var res = await client.ConnectAsync(
                                mqttOptions,
                                cancel);

                            log.LogInformation(
                                $"MQTT connect result {res.ResultCode}");

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
                                "MQTT connection failed");
                        }

                        await Task.Delay(
                            TimeSpan.FromSeconds(5),
                            cancel);
                    }

                    //
                    // Home Assistant discovery
                    //
                    if (!autoConfigPerformed && autoDiscoveryEnabled)
                    {
                        await AutoDiscovery(client, cancel);
                        autoConfigPerformed = true;
                    }

                    //
                    // Publish pending values
                    //
                    while (client.IsConnected &&
                           !cancel.IsCancellationRequested)
                    {
                        if (pending.IsEmpty)
                        {
                            await pendingSignal.WaitAsync(cancel);
                        }

                        foreach (var msg in pending.ToArray())
                        {
                            if (!client.IsConnected)
                            {
                                break;
                            }

                            try
                            {
                                log.LogDebug(
                                    $"Publishing to topic {msg.Key}");

                                await client.PublishAsync(
                                    new MqttApplicationMessage
                                    {
                                        Topic = msg.Key,
                                        Payload = msg.Value,
                                        Retain = true
                                    },
                                    cancel);

                                /*
                                 * Remove only this exact value.
                                 *
                                 * If Reg_ValueChanged replaced it with a
                                 * newer byte[] while PublishAsync was
                                 * running, Remove() fails and the newer
                                 * value remains pending.
                                 */
                                ((ICollection<
                                    KeyValuePair<string, byte[]>>)pending)
                                    .Remove(msg);
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
                                    $"MQTT publish failed for {msg.Key}");

                                // Leave the value in pending.
                                // Reconnect and try again later.
                                break;
                            }
                        }
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
                        "MQTT connection lost, reconnecting");
                }

                if (!cancel.IsCancellationRequested)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(5),
                        cancel);
                }
            }
        }
        finally
        {
            //
            // Remove handlers only when Publish() actually terminates,
            // not when MQTT reconnects.
            //
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
            $"Mqtt register {r.Topic} value changed: {r.Value}");

        if (r.Value != null && r.Topic != null)
        {
            var path = mqttTopicRoot + "/" + r.Topic;
            var payload = Encoding.UTF8.GetBytes(r.Value);

            //
            // Replace an older unpublished value for this topic.
            //
            pending[path] = payload;

            //
            // Wake the publisher if it is waiting.
            //
            if (pendingSignal.CurrentCount == 0)
            {
                pendingSignal.Release();
            }
        }
    }

    async Task AutoDiscovery(
        IApplicationMessagePublisher client,
        CancellationToken cancel)
    {
        foreach (var reg in appConfig.ToMqtt
                     .Where(r => r.AutoDiscoveryEnabled))
        {
            var settings = appConfig.AllRegConfigs
                .Single(r => r.topic == reg.Topic);

            var uniqueId =
                $"Pelican{deviceSerialNumber}_{reg.ObjectId}";

            var configTopic =
                $"homeassistant/{reg.HomeAssistantPlatform}/{uniqueId}/config";

            var device = new
            {
                manufacturer = "Enervent",
                name = "Pelican",
                model = "ACE-CG",
                identifiers = new[]
                {
                    "Pelican" + deviceSerialNumber
                }
            };

            object autoConfig;

            if (reg.HomeAssistantPlatform == "sensor")
            {
                autoConfig = new
                {
                    state_topic = mqttTopicRoot + "/" + reg.Topic,
                    unit_of_measurement =
                        reg.HomeAssistantUnitOfMeasurement,
                    value_template = "{{ value }}",
                    device_class = reg.HomeAssistantDeviceClass,
                    settings.name,
                    device,
                    unique_id = uniqueId
                };
            }
            else if (reg.HomeAssistantPlatform == "number")
            {
                autoConfig = new
                {
                    state_topic = mqttTopicRoot + "/" + reg.Topic,
                    command_topic =
                        mqttTopicRoot + "/" + reg.Topic + "/cmd",
                    unit_of_measurement =
                        reg.HomeAssistantUnitOfMeasurement,
                    value_template = "{{ value }}",
                    device_class = reg.HomeAssistantDeviceClass,
                    settings.name,
                    min = reg.Min,
                    max = reg.Max,
                    device,
                    unique_id = uniqueId,
                    entity_category = "config"
                };
            }
            else if (reg.HomeAssistantPlatform == "switch")
            {
                autoConfig = new
                {
                    state_topic = mqttTopicRoot + "/" + reg.Topic,
                    command_topic =
                        mqttTopicRoot + "/" + reg.Topic + "/cmd",
                    settings.name,
                    device,
                    unique_id = uniqueId,
                    entity_category = "config"
                };
            }
            else // binary_sensor
            {
                autoConfig = new
                {
                    state_topic = mqttTopicRoot + "/" + reg.Topic,
                    device_class = reg.HomeAssistantDeviceClass,
                    settings.name,
                    device,
                    unique_id = uniqueId,
                    entity_category = "diagnostic"
                };
            }

            await client.PublishAsync(
                new MqttApplicationMessage
                {
                    Topic = configTopic,
                    Payload = Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(autoConfig)),
                    Retain = true
                },
                cancel);
        }
    }
}