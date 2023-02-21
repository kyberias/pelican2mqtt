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
                var reg = new PelicanByteRegister((byte)r.address, (byte)r.index, r.writable ? RegAccess.ReadWrite : RegAccess.ReadOnly, r.type);
                if (r.bits != null && r.bits.Any())
                {
                    var bits = r.bits.Select(br => ((IRegister)new PelicanBitRegister(reg, br.bit, br.type, br.writable), (RegisterConfigCommon)br));
                    return bits.Concat(new[] { ((IRegister)reg, (RegisterConfigCommon)r) });
                }

                return new[] { ((IRegister)reg, (RegisterConfigCommon)r) };
            }).ToList();

        var supplyFan = registers.Single(r => r.Item2.topic == "fans/supply/speed").Item1;
        var exhaustFan = registers.Single(r => r.Item2.topic == "fans/exhaust/speed").Item1;

        var power = new PowerConsumptionCalculator((PelicanByteRegister)supplyFan, (PelicanByteRegister)exhaustFan);

        var allRegConfigs = regs.SelectMany(c => new[] { c }.Concat(c.bits != null ? c.bits : new RegisterConfigCommon[] { })).ToList();

        allRegConfigs.Add(new RegisterConfigCommon
        {
            topic = power.Topic,
            name = "Power"
        });
        AllRegConfigs = allRegConfigs;

        ToMqtt = registers
            .Where(r => !string.IsNullOrEmpty(r.Item2.topic))
            .Select(r =>
                r.Item1 is IByteRegister
                    ? (IMqttRegister)new MqttByteRegister(r.Item2.topic, (IByteRegister)r.Item1, log, r.Item2.autoDiscovery)
                    : new MqttBitRegister(r.Item2.topic, (IBitRegister)r.Item1, r.Item2.autoDiscovery))
            .ToList();

        ToMqtt.Add(power);

        Registers = registers;
    }

    public IList<(IRegister,RegisterConfigCommon)> Registers { get; }

    public IList<IMqttRegister> ToMqtt { get; }

    public IEnumerable<RegisterConfigCommon> AllRegConfigs { get; }
}
