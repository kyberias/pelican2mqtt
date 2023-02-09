using System;
using Microsoft.Extensions.Logging;

namespace pelican2mqtt.Pelican;

class MqttByteRegister : IMqttRegister
{
    private ILogger log;

    public MqttByteRegister(string address, IByteRegister reg, ILogger log)
    {
        Address = address;
        this.reg = reg;
        this.log = log;

        reg.ValueChanged += Reg_ValueChanged;
    }

    private void Reg_ValueChanged(object sender, EventArgs e)
    {
        log.LogDebug("Byte register value changed.");
        ValueChanged.Invoke(this, EventArgs.Empty);
    }

    private readonly IByteRegister reg;

    public string Address { get; }

    public string Value
    {
        get
        {
            switch (reg.Unit)
            {
                case RegUnit.Celsius:
                    int temperature = ConvertTwosComplementByteToInteger(reg.Data.GetValueOrDefault());
                    return temperature.ToString();
                case RegUnit.Percentage:
                case RegUnit.VentilationSpeed:
                    return reg.Data.GetValueOrDefault().ToString();
                case RegUnit.PercentageOfMaximum:
                    return ((int)(reg.Data.GetValueOrDefault() / 255.0 * 100)).ToString();
                default:
                    throw new NotSupportedException(reg.Unit.ToString());
            }
        }
    }

    public event EventHandler ValueChanged = delegate { };

    private static int ConvertTwosComplementByteToInteger(byte rawValue)
    {
        // If a positive value, return it
        if ((rawValue & 0x80) == 0)
        {
            return rawValue;
        }

        // Otherwise perform the 2's complement math on the value
        return (byte)~(rawValue - 0x01) * -1;
    }
}