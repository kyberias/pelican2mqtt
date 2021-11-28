using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using CommandLine;
using CommandLine.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using NLog;

namespace ModbusDump
{
    enum RegAccess
    {
        ReadOnly,
        ReadWrite
    };

    enum RegUnit
    {
        Unknown,
        Celsius, // two's complement byte
        Pascal,
        Minutes,
        VentilationSpeed, // 1 - 6
        Percentage, // 100 = 100%
        PercentageOfMaximum, // 0xFF = 100%,
        Index,
        WeekdaysBitfield,
        BitField,
        Time // value = hours * 8 + 1 * (each 15 minutes past the hours). E.g. 18.30 = 18 * 8 + 2 = 146 (0x92)
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Options options = null;

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    options = o;
                });

            if (options == null)
            {
                var help = HelpText.AutoBuild(result);
                Console.WriteLine(help.ToString());
                return;
            }

            if (!options.Server)
            {
                if (string.IsNullOrEmpty(options.MqttServer) || string.IsNullOrEmpty(options.MqttLogin) ||
                    string.IsNullOrEmpty(options.MqttPassword))
                {
                    Console.WriteLine("MQTT settings required when not running as a server.");
                    return;
                }
            }

            if (options.Server && options.Client)
            {
                Console.WriteLine("Cannot be both client and server.");
                return;
            }
            
            var config = new NLog.Config.LoggingConfiguration();

            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "file.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

            LogManager.Configuration = config;

            var log = LogManager.GetCurrentClassLogger();
            
            log.Info("Started");

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
                            log.Error(ex);
                            break;
                        }
                    }

                    port.Close();
                }
            }

            async Task SendToTcpClient(ISourceBlock<byte> source, CancellationToken cancel)
            {
                var ipAddress = IPAddress.Parse(options.Host);

                //IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, options.Port);

                log.Info($"Local IP: {ipAddress}");

                Socket listener = new Socket(ipAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);

                listener.Bind(localEndPoint);
                listener.Listen(10);

                // Start listening for connections.  
                while (true)
                {
                    // Program is suspended while waiting for an incoming connection.  
                    
                    Socket handler = listener.Accept();

                    var buf = new byte[1];
                    
                    // An incoming connection needs to be processed.  
                    while (true)
                    {
                        buf[0] = await source.ReceiveAsync(cancel);

                        try
                        {
                            handler.Send(buf);
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                            break;
                        }
                    }

                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }
            }

            async Task SocketReader(ITargetBlock<byte> target, CancellationToken cancel)
            {
                Socket s = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);

                s.Connect(options.Host, options.Port);

                var stream = new NetworkStream(s);

                var buf = new byte[1];
                
                while (true)
                {
                    await stream.ReadAsync(buf, 0, 1);
                    target.Post(buf[0]);
                }
            }

            var byteBuffer = new BufferBlock<byte>();
            var messages = new BufferBlock<byte[]>();

            var registersToDisplay = new[]
            {
                0x47, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xeb, 0xec, 0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf2, 0xf3
            };
            
            /*Console.Clear();
            foreach (var row in registersToDisplay)
            {
                Console.WriteLine($"{row:X2}");
            }*/

            var roomTemperature =
                new Register(0x47, 0, "temperatures/room", RegAccess.ReadOnly, RegUnit.Celsius);

            var supplyTemperatureAfterHeatRecoveryAndHeatingAndCooling =
                new Register(0xE6, 0, "temperatures/supplyAfterHeatRecoveryAndHeatingAndCooling", RegAccess.ReadOnly, RegUnit.Celsius);

            var exhaustTemperature =
                new Register(0xE6, 1, "temperatures/exhaust", RegAccess.ReadOnly, RegUnit.Celsius);

            var wasteTemperature =
                new Register(0xE6, 2, "temperatures/waste", RegAccess.ReadOnly, RegUnit.Celsius);

            var outsideTemperature =
                new Register(0xE6, 3, "temperatures/outside", RegAccess.ReadOnly, RegUnit.Celsius);

            var returnWaterTemperature =
                new Register(0xE6, 4, "temperatures/returnWater", RegAccess.ReadOnly, RegUnit.Celsius);

            var supplyTemperatureAfterHeatRecovery =
                new Register(0xE6, 5, "temperatures/supplyAfterHeatRecovery", RegAccess.ReadOnly, RegUnit.Celsius);

            var supplyAirTempPreset = new Register(0xE7, 0, "settings/temperatures/supply", RegAccess.ReadWrite,
                RegUnit.Celsius);

            var heatRecoveryEfficiency =
                new Register(0xF1, 1, "heatRecovery/efficiency", RegAccess.ReadOnly, RegUnit.Percentage);
            var coolingPercentage = 
                new Register(0xF1, 3, "cooling/actuator", RegAccess.ReadOnly, RegUnit.PercentageOfMaximum);

            var humidity =
                new Register(0xEA, 3, "humidity/roomRelativeHumidity", RegAccess.ReadOnly, RegUnit.Percentage);
            
            var regF1_0 =
                new Register(0xF1, 0, null, RegAccess.ReadOnly, RegUnit.BitField);

            var heatRecoveryWheelOn = new BitRegister(regF1_0, 2, "hrw/rotation");
            var heaterOn = new BitRegister(regF1_0, 3, "heater/status");
            var overPressureActive = new BitRegister(regF1_0, 4, "overPressure/status");

            var regF2_3 =
                new Register(0xF2, 3, null, RegAccess.ReadOnly, RegUnit.BitField);

            var heaterLedState = new BitRegister(regF2_3, 0, "heaterOrCoolingStatus");
            
            var cookerHood = new BitRegister(regF2_3, 2, "cookerHood/status");
            var centralVacuumCleaner = new BitRegister(regF2_3, 3, "centralVacuumCleaner/status");

            var supplyFanSpeed = new Register(0xF0, 1, "fans/supply/speed", RegAccess.ReadOnly, RegUnit.VentilationSpeed);
            var exhaustFanSpeed = new Register(0xF0, 2, "fans/exhaust/speed", RegAccess.ReadOnly, RegUnit.VentilationSpeed);

            var mqttTopicRoot = "pelicanAC";

            var toMqtt = new IRegister[]
            {
                roomTemperature,
                outsideTemperature,
                exhaustTemperature,
                wasteTemperature,
                returnWaterTemperature,
                supplyAirTempPreset,
                supplyTemperatureAfterHeatRecovery,
                supplyTemperatureAfterHeatRecoveryAndHeatingAndCooling,
                heatRecoveryEfficiency,
                humidity,
                heatRecoveryWheelOn,
                heaterOn,
                overPressureActive,
                heaterLedState,
                cookerHood,
                centralVacuumCleaner,
                supplyFanSpeed,
                exhaustFanSpeed,
                coolingPercentage
            };

            var registers = new[]
            {
                coolingPercentage,
                roomTemperature,
                wasteTemperature,
                exhaustTemperature,
                outsideTemperature,
                returnWaterTemperature,
                supplyTemperatureAfterHeatRecovery,
                supplyTemperatureAfterHeatRecoveryAndHeatingAndCooling,
                supplyAirTempPreset,

                new Register(0xE8, 0, "Puhallinnopeus Tulo asetus", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xE8, 1, "Puhallinnopeus Poisto asetus", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xE8, 2, "Aikaohjaus Tulo", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xE8, 3, "Aikaohjaus Poisto", RegAccess.ReadWrite, RegUnit.VentilationSpeed),

                humidity,
                new Register(0xEA, 4, "RH raja %", RegAccess.ReadWrite, RegUnit.Percentage),
                new Register(0xEA, 5, "RH ohjaus säätöväli min", RegAccess.ReadWrite, RegUnit.Minutes),

                new Register(0xEB, 0, "RH tehostus Tulo nopeus", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xEB, 1, "RH tehostus Poisto nopeus", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xEB, 2, "RH tehostus Tulo paine", RegAccess.ReadWrite, RegUnit.VentilationSpeed),
                new Register(0xEB, 3, "RH tehostus Poisto paine", RegAccess.ReadWrite, RegUnit.VentilationSpeed),

                new Register(0xEC, 2, "Aikaohjelman numero", RegAccess.ReadWrite, RegUnit.Index),
                new Register(0xEC, 3, "Aikaohjelman viikonpäivät", RegAccess.ReadWrite, RegUnit.WeekdaysBitfield),
                new Register(0xEC, 4, "Aikaohjelman aloitusaika", RegAccess.ReadWrite, RegUnit.Time),
                new Register(0xEC, 5, "Aikaohjelman lopetusaika", RegAccess.ReadWrite, RegUnit.Time),

                new Register(0xED, 2, "Tulo kuuma raja", RegAccess.ReadWrite, RegUnit.Celsius),
                new Register(0xED, 4, "Poisto kylmä raja", RegAccess.ReadWrite, RegUnit.Celsius),
                new Register(0xED, 5, "LTO kesä raja", RegAccess.ReadWrite, RegUnit.Celsius),

                new Register(0xEE, 2, "Suodatin raja Pa", RegAccess.ReadWrite, RegUnit.Pascal),

                new Register(0xF0, 3, "Ylipaine aika min.", RegAccess.ReadOnly, RegUnit.Minutes),
                heatRecoveryEfficiency,
                supplyFanSpeed,
                exhaustFanSpeed,
                regF1_0,

                new Register(0xF2, 1, "Tehostuksen kesto min", RegAccess.ReadWrite, RegUnit.Minutes),
            };

            var regFile = registers.ToDictionary(r => (r.Address, r.Index), r => r);
            
            var mqttQueue = new BufferBlock<(string, byte[])>();
            
            async Task MqttPublisher(CancellationToken cancel)
            {
                var mqttOptions = new MqttClientOptionsBuilder()
                    .WithClientId("EnerventAC")
                    .WithTcpServer(options.MqttServer)
                    .WithCredentials(options.MqttLogin, options.MqttPassword)
                    .Build();

                var factory = new MqttFactory();

                while (true)
                {
                    using (var client = factory.CreateMqttClient())
                    {
                        try
                        {
                            var res = await client.ConnectAsync(mqttOptions, cancel);
                            log.Info($"MQTT connect result {res.ResultCode}");

                            while (true)
                            {
                                await mqttQueue.OutputAvailableAsync(cancel);

                                if (!client.IsConnected)
                                {
                                    break;
                                }
                                
                                var msg = await mqttQueue.ReceiveAsync(cancel);

                                await client.PublishAsync(msg.Item1, msg.Item2);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                    }
                }
            }
            
            async Task Process(ISourceBlock<byte[]> msgs, CancellationToken cancel)
            {
                foreach (var reg in toMqtt)
                {
                    reg.ValueChanged += (sender, eventArgs) =>
                    {
                        var r = (IRegister)sender;
                        if (r.Value != null && r.Address != null)
                        {
                            var path = mqttTopicRoot + "/" + r.Address;
                            mqttQueue.Post((path, Encoding.ASCII.GetBytes(r.Value)));
                            //var publishResult = await client.PublishAsync(path, Encoding.ASCII.GetBytes(r.Value));
                            //log.Debug($"Published {path} <- {r.Value}. Result: {publishResult.ReasonCode}");
                        }

                        /*if (r != null && r.Unit == RegUnit.Bit && r.On.HasValue)
                        {
                            await client.PublishAsync(mqttTopicRoot + "/" + r.Name, r.On.Value ? "on" : "off");
                        }*/
                    };
                }

                while (true)
                {
                    var msg = await msgs.ReceiveAsync(cancel);
                    var addr = msg[0];
                    var msglen = msg.Length;

                    var row = Array.IndexOf(registersToDisplay, addr);

                    for (byte i = 0; i < msglen-1; i++)
                    {
                        if (!regFile.ContainsKey((addr, i)))
                        {
                            regFile[(addr, i)] = new Register(addr, i, null, RegAccess.ReadWrite, RegUnit.Unknown);
                        }

                        var reg = regFile[(addr, i)];

                        /*var color = reg.Name != null
                            ? (reg.Access == RegAccess.ReadOnly ? ConsoleColor.Green : ConsoleColor.Cyan)
                            : ConsoleColor.Gray;*/

                        var old = reg.Data;

                        reg.Data = msg[i + 1];

                        /*if (row >= 0)
                        {
                            if (old != reg.Data)
                            {
                                Console.SetCursorPosition(3 + i * 3, row);
                                Console.ForegroundColor = color;
                                Console.Write($"{msg[i + 1]:X2}");
                            }
                        }
                        else
                        {
                            log.Warn($"Unknown register {addr:X2}, len = {msglen}");
                        }*/
                    }
                }
            }

            using (var cancel = new CancellationTokenSource())
            {
                var reader = options.Client
                    ? SocketReader(byteBuffer, cancel.Token)
                    : SerialPortReader(byteBuffer, options.SerialPort, cancel.Token);

                var processor = options.Server
                    ? SendToTcpClient(byteBuffer, cancel.Token)
                    : EnerventProtocolParser.Parse(byteBuffer, messages, cancel.Token);

                Task[] tasks;

                if (!options.Server)
                {
                    var msgProcessor = Process(messages, cancel.Token);
                    var publisher = MqttPublisher(cancel.Token);

                    tasks = new[] { msgProcessor, reader, processor, publisher };
                }
                else
                {
                    tasks = new[] { reader, processor };
                }

                await Task.WhenAny(tasks);
                
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
}
