﻿using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;

namespace pelican2mqtt.Mqtt;

class MqttPublisher
{
    private readonly IMqttFactory factory;
    private readonly IConfiguration config;
    private readonly BufferBlock<(string topic, byte[] payload)> mqttQueue;
    private readonly ILogger<MqttPublisher> log;
    private readonly ApplicationConfig appConfig;
    private readonly string mqttTopicRoot;
    private readonly string deviceSerialNumber;

    public MqttPublisher(IMqttFactory factory, IConfiguration config, ILogger<MqttPublisher> log, ApplicationConfig appConfig)
    {
        this.factory = factory;
        this.config = config;
        mqttQueue = new BufferBlock<(string topic, byte[] payload)>();
        this.log = log;
        this.appConfig = appConfig;

        mqttTopicRoot = config["mqtt:baseTopic"];
        deviceSerialNumber = config["deviceSerialNumber"];
    }

    public async Task Publish(CancellationToken cancel)
    {
        foreach (var reg in appConfig.ToMqtt)
        {
            reg.ValueChanged += Reg_ValueChanged;
        }

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

        while (true)
        {
            using var client = factory.CreateMqttClient();

            try
            {
                while (true)
                {
                    var res = await client.ConnectAsync(mqttOptions, cancel);
                    log.LogInformation($"MQTT connect result {res.ResultCode}");
                    if (res.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cancel);
                }

                if (!autoConfigPerformed)
                {
                    await AutoDiscovery(client, cancel);
                    autoConfigPerformed = true;
                }

                while (true)
                {
                    await mqttQueue.OutputAvailableAsync(cancel);

                    if (!client.IsConnected)
                    {
                        break;
                    }

                    var msg = await mqttQueue.ReceiveAsync(cancel);

                    log.LogDebug($"Publishing to topic " + msg.topic);

                    await client.PublishAsync(msg.topic, msg.payload);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, ex.Message);
                throw;
            }
            finally
            {
                foreach (var reg in appConfig.ToMqtt)
                {
                    reg.ValueChanged -= Reg_ValueChanged;
                }
            }
        }
    }

    private void Reg_ValueChanged(object sender, EventArgs e)
    {
        var r = (IMqttRegister)sender;
        log.LogDebug($"Mqtt register {r.Topic} value changed: {r.Value}");
        if (r.Value != null && r.Topic != null)
        {
            var path = mqttTopicRoot + "/" + r.Topic;
            mqttQueue.Post((path, Encoding.ASCII.GetBytes(r.Value)));
        }
    }

    async Task AutoDiscovery(IMqttClient client, CancellationToken cancel)
    {
        foreach (var reg in appConfig.ToMqtt.Where(r => r.AutoDiscoveryEnabled))
        {
            var settings = appConfig.AllRegConfigs.Single(r => r.topic == reg.Topic);

            var autoConfig = new
            {
                state_topic = mqttTopicRoot + "/" + reg.Topic,
                unit_of_measurement = reg.HomeAssistantUnitOfMeasurement,
                value_template = reg.HomeAssistantPlatform == "sensor" ? "{{ value }}" : "",
                device_class = reg.HomeAssistantDeviceClass,
                settings.name,
                device = new
                {
                    manufacturer = "Enervent",
                    name = "Pelican",
                    model = "ACE-CG",
                    identifiers = new[]
                    {
                        "Pelican" + deviceSerialNumber
                    }
                },
                unique_id = $"Pelican{deviceSerialNumber}_{reg.ObjectId}"
            };

            var configTopic = $"homeassistant/{reg.HomeAssistantPlatform}/{autoConfig.unique_id}/config";

            await client.PublishAsync(new MqttApplicationMessage
            {
                Topic = configTopic,
                Payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(autoConfig)),
                Retain = true
            });
        }
    }
}