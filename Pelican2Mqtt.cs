using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;
using pelican2mqtt.Pelican;

namespace pelican2mqtt;

class Pelican2Mqtt
{
    private readonly IConfiguration config;
    private readonly ILogger<Pelican2Mqtt> log;
    private readonly IMqttFactory factory;

    public Pelican2Mqtt(IConfiguration config, ILogger<Pelican2Mqtt> log, IMqttFactory factory)
    {
        this.config = config;
        this.log = log;
        this.factory = factory;
    }

    public async Task Run(CancellationToken stoppingToken)
    {
        Options options = new Options
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

        async Task SerialPortReader(ITargetBlock<byte> output, string portName, CancellationToken cancel)
        {
            using (SerialPort port = new SerialPort(portName)
                   {
                       BaudRate = 19200,
                       DataBits = 8,
                       StopBits = StopBits.Two,
                       Parity = Parity.None
                   })
            {
                var buf = new byte[1];

                port.Open();

                while (true)
                {
                    try
                    {
                        await port.BaseStream.ReadAsync(buf, 0, 1, cancel);
                        output.Post(buf[0]);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, ex.Message);
                        break;
                    }
                }

                port.Close();
            }
        }

        var byteBuffer = new BufferBlock<byte>();
        var messages = new BufferBlock<byte[]>();

        var registersSection = config.GetSection("registers");
        var regs = registersSection.Get<RegisterConfig[]>();

        var registers = regs
            .SelectMany(r =>
            {
                var reg = new PelicanByteRegister((byte)r.address, (byte)r.index, RegAccess.ReadOnly, r.type);
                if (r.bits != null && r.bits.Any())
                {
                    var bits = r.bits.Select(br => ((IRegister)new PelicanBitRegister(reg, br.bit), br.topic));
                    return bits.Concat(new[] { ((IRegister)reg,r.topic) });
                }

                return new[] { ((IRegister)reg,r.topic) };
            }).ToList();

        var toMqtt = registers
            .Where(r => !string.IsNullOrEmpty(r.topic))
            .Select(r =>
                r.Item1 is IByteRegister
                    ? (IMqttRegister)new MqttByteRegister(r.topic, (IByteRegister)r.Item1, log)
                    : new MqttBitRegister(r.topic, (IBitRegister)r.Item1))
            .ToList();

        var mqttTopicRoot = config["mqtt:baseTopic"];

        var regFile = new ConcurrentDictionary<(byte, byte), IByteRegister>(
            registers
                .Where(r => r.Item1 is IByteRegister)
                .Select(r => (IByteRegister)r.Item1)
                .ToDictionary(r => (r.Address, r.Index), r => r));

        var mqttQueue = new BufferBlock<(string topic, byte[] payload)>();

        async Task MqttPublisher(CancellationToken cancel)
        {
            var mqttOptions = new MqttClientOptionsBuilder()
                .WithClientId("EnerventAC")
                .WithTcpServer(options.MqttServer)
                .WithCredentials(options.MqttLogin, options.MqttPassword)
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
            }
        }

        async Task ProcessMessagesFromPelican(ISourceBlock<byte[]> msgs, CancellationToken cancel)
        {
            foreach (var reg in toMqtt)
            {
                reg.ValueChanged += (sender, eventArgs) =>
                {
                    var r = (IMqttRegister)sender;
                    log.LogDebug($"Mqtt register {r.Address} value changed: {r.Value}");
                    if (r.Value != null && r.Address != null)
                    {
                        var path = mqttTopicRoot + "/" + r.Address;
                        mqttQueue.Post((path, Encoding.ASCII.GetBytes(r.Value)));
                    }
                };
            }

            while (true)
            {
                var msg = await msgs.ReceiveAsync(cancel);
                var addr = msg[0];
                var msglen = msg.Length;

                log.LogDebug("Message received");

                for (byte i = 0; i < msglen - 1; i++)
                {
                    if (!regFile.ContainsKey((addr, i)))
                    {
                        regFile[(addr, i)] = new PelicanByteRegister(addr, i, RegAccess.ReadWrite, RegUnit.Unknown);
                    }

                    var reg = regFile[(addr, i)];

                    reg.Data = msg[i + 1];
                }
            }
        }

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            var reader = SerialPortReader(byteBuffer, options.SerialPort, cancel.Token);

            var processor = EnerventProtocolParser.Parse(log, byteBuffer, messages, cancel.Token);

            Task publisher = null;
            Task msgProcessor = null;

            msgProcessor = ProcessMessagesFromPelican(messages, cancel.Token);
            publisher = MqttPublisher(cancel.Token);

            var tasks = new[] { msgProcessor, reader, processor, publisher };

            while (true)
            {
                await Task.WhenAny(tasks);

                if (publisher != null && publisher.IsFaulted && !publisher.IsCanceled)
                {
                    // Make sure we resend all the values after next reconnect
                    foreach (var reg in regFile)
                    {
                        reg.Value.ClearOldValue();
                    }

                    try
                    {
                        await publisher;
                    }
                    catch { }

                    publisher = MqttPublisher(cancel.Token);
                    tasks = new[] { msgProcessor, reader, processor, publisher };
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