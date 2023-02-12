using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace pelican2mqtt.Pelican;

class EnerventMessageProcessor
{
    private readonly ApplicationConfig appConfig;
    private readonly ILogger<EnerventMessageProcessor> log;

    public EnerventMessageProcessor(ApplicationConfig appConfig, ILogger<EnerventMessageProcessor> log)
    {
        this.appConfig = appConfig;
        this.log = log;
    }

    public async Task ProcessMessagesFromPelican(ISourceBlock<byte[]> msgs, CancellationToken cancel)
    {
        var regFile = new ConcurrentDictionary<(byte, byte), IByteRegister>(
            appConfig.Registers
                .Where(r => r.Item1 is IByteRegister)
                .Select(r => (IByteRegister)r.Item1)
                .ToDictionary(r => (r.Address, r.Index), r => r));

        while (true)
        {
            var msg = await msgs.ReceiveAsync(cancel);
            var addr = msg[0];
            var msglen = msg.Length;

            for (byte i = 0; i < msglen - 1; i++)
            {
                if (!regFile.ContainsKey((addr, i)))
                {
                    regFile[(addr, i)] = new PelicanByteRegister(addr, i, RegAccess.ReadWrite, RegUnit.Unknown);
                }

                var reg = regFile[(addr, i)];

                reg.Data = msg[i + 1];
            }

            log.LogDebug($"Message for address {addr} received, len {msglen}");
        }
    }
}