using System;
using Microsoft.Extensions.Logging;

namespace pelican2mqtt.Pelican;

class MqttByteRegister : IMqttRegister
{
    private readonly ILogger log;

    public MqttByteRegister(string topic, IByteRegister reg, ILogger log, bool autoDiscoveryEnabled)
    {
        AutoDiscoveryEnabled = autoDiscoveryEnabled;
        Topic = topic;
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

    public string Topic { get; }

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
                case RegUnit.RelativeHumidity:
                    return reg.Data.GetValueOrDefault().ToString();
                case RegUnit.PercentageOfMaximum:
                    return ((int)(reg.Data.GetValueOrDefault() / 255.0 * 100)).ToString();
                default:
                    throw new NotSupportedException(reg.Unit.ToString());
            }
        }
    }

    public event EventHandler ValueChanged = delegate { };

    public RegUnit Unit => reg.Unit;
    public string ObjectId => $"register_{reg.Address:X2}_{reg.Index}";
    public bool AutoDiscoveryEnabled { get; }

    public string HomeAssistantPlatform => Writable ? "number" : "sensor";

    public string HomeAssistantDeviceClass => reg.Unit == RegUnit.Celsius
        ? "temperature"
        : (reg.Unit == RegUnit.RelativeHumidity ? "humidity" : null);

    public string HomeAssistantUnitOfMeasurement =>
        reg.Unit == RegUnit.Celsius ? "°C" : (reg.Unit != RegUnit.VentilationSpeed ? "%" : "");

    public bool Writable => reg.Writable;
    public int Min => reg.Unit == RegUnit.VentilationSpeed ? 1 : 0;
    public int Max => reg.Unit == RegUnit.VentilationSpeed ? 6 : 100;

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