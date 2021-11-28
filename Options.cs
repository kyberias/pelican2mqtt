using CommandLine;

namespace ModbusDump
{
    public class Options
    {
        [Option('s', "server", Required = false, HelpText = "Open a TCP server to share the serial protocol traffic.")]
        public bool Server { get; set; }

        [Option('c', "client", Required = false, HelpText = "Connect to TCP server instead of serial port.")]
        public bool Client { get; set; }

        [Option('h', "host", Required = false, HelpText = "Host name of the server to connect.")]
        public string Host { get; set; }

        [Option('p', "port", Required = false, Default = 8990, HelpText = "TCP port to connect or listen.")]
        public int Port { get; set; }

        [Option('i', "serialport", Required = false, HelpText = "Serial port name.")]
        public string SerialPort { get; set; }

        [Option('m', "mqttserver", Required = false, HelpText = "MQTT server name")]
        public string MqttServer { get; set; }

        [Option('l', "mqttlogin", Required = false, HelpText = "MQTT login name")]
        public string MqttLogin { get; set; }

        [Option('w', "mqttpassword", Required = false, HelpText = "MQTT password")]
        public string MqttPassword { get; set; }
    }
}
