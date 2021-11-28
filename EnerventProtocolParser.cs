﻿using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using NLog;

namespace ModbusDump
{
    static class EnerventProtocolParser
    {
        /// <summary>
        /// Parses a stream of serial port bytes to messages (byte array) with verified checksum.
        /// </summary>
        public static async Task Parse(ISourceBlock<byte> source, ITargetBlock<byte[]> target, CancellationToken cancel)
        {
            var log = LogManager.GetCurrentClassLogger();

            while (true)
            {
                byte b = 0;

                while (b != 0x80)
                {
                    b = await source.ReceiveAsync(cancel);
                }

                var addr = await source.ReceiveAsync(cancel);
                if (addr != 0x01)
                {
                    log.Warn($"Unexpected ADDR: {addr:X2}");
                }
                addr = await source.ReceiveAsync(cancel);
                if (addr != 0x01)
                {
                    log.Warn($"Unexpected ADDR: {addr:X2}");
                }

                var len = await source.ReceiveAsync(cancel);
                var originalLen = len;
                len -= 6;
                var buf = new byte[len];
                var n = 0;

                while (n < len)
                {
                    b = await source.ReceiveAsync(cancel);

                    if (b == 0x82)
                    {
                        b = await source.ReceiveAsync(cancel);
                        var reversed = (byte)~b;
                        //log.Debug($"Inverted {b:X2} to {reversed:X2}");
                        b = reversed;
                    }

                    buf[n] = b;
                    n++;
                }

                var crc = await source.ReceiveAsync(cancel);
                byte sum = (byte)(0x01 + 0x01 + len + 6);
                for (int i = 0; i < len; i++)
                {
                    sum += buf[i];
                }

                if (crc != sum)
                {
                    log.Warn($"CRC error: len={originalLen:X2} crc={crc:X2} {string.Join(" ", buf.Select(b => b.ToString("X2")))}");
                    continue;
                }

                var etx = await source.ReceiveAsync(cancel);

                if (etx != 0x81)
                {
                    log.Warn($"Incorrect ETX: {etx:X2}");
                }

                target.Post(buf);
            }
        }
    }
}
