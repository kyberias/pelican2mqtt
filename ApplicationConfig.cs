using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using pelican2mqtt.Pelican;

namespace pelican2mqtt;

class ApplicationConfig
{
    public ApplicationConfig(IConfiguration config, ILogger<ApplicationConfig> log)
    {
        var registersSection = config.GetSection("registers");
        var regs = registersSection.Get<RegisterConfig[]>();

        var registers = regs
            .SelectMany(r =>
            {
                var reg = new PelicanByteRegister((byte)r.address, (byte)r.index, RegAccess.ReadOnly, r.type);
                if (r.bits != null && r.bits.Any())
                {
                    var bits = r.bits.Select(br => ((IRegister)new PelicanBitRegister(reg, br.bit, br.type), (RegisterConfigCommon)br));
                    return bits.Concat(new[] { ((IRegister)reg, (RegisterConfigCommon)r) });
                }

                return new[] { ((IRegister)reg, (RegisterConfigCommon)r) };
            }).ToList();

        AllRegConfigs = regs.SelectMany(c => new[] { c }.Concat(c.bits != null ? c.bits : new RegisterConfigCommon[] { }));

        ToMqtt = registers
            .Where(r => !string.IsNullOrEmpty(r.Item2.topic))
            .Select(r =>
                r.Item1 is IByteRegister
                    ? (IMqttRegister)new MqttByteRegister(r.Item2.topic, (IByteRegister)r.Item1, log, r.Item2.autoDiscovery)
                    : new MqttBitRegister(r.Item2.topic, (IBitRegister)r.Item1, r.Item2.autoDiscovery))
            .ToList();

        Registers = registers;
    }

    public IList<(IRegister,RegisterConfigCommon)> Registers { get; }

    public IList<IMqttRegister> ToMqtt { get; }

    public IEnumerable<RegisterConfigCommon> AllRegConfigs { get; }
}
