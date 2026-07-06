using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace pelican2mqtt.Serial;

class SerialPortReader
{
    private readonly ILogger<SerialPortReader> log;

    public SerialPortReader(ILogger<SerialPortReader> log)
    {
        this.log = log;
    }

    public async Task ReadBytesFromPort(ITargetBlock<byte> output, string portName, CancellationToken cancel)
    {
        using (var port = new SerialPort(portName)
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
}