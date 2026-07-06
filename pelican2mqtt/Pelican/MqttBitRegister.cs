using System;

namespace pelican2mqtt.Pelican;

class MqttBitRegister : IMqttRegister
{
    private readonly IBitRegister reg;

    public MqttBitRegister(string topic, IBitRegister reg, bool autoDiscoveryEnabled)
    {
        this.reg = reg;
        AutoDiscoveryEnabled = autoDiscoveryEnabled;
        reg.ValueChanged += Reg_ValueChanged;
        Topic = topic;
    }

    private void Reg_ValueChanged(object sender, EventArgs e)
    {
        ValueChanged.Invoke(this, EventArgs.Empty);
    }

    public string Topic { get; }
    public string Value
    {
        get
        {
            if (reg.On == null)
            {
                return null;
            }

            return reg.On == true ? "ON" : "OFF";
        }
    }
    public event EventHandler ValueChanged = delegate { };
    public RegUnit Unit => RegUnit.OnOff;
    public string ObjectId => $"register_{reg.Address:X2}_{reg.Index}_{reg.Bit}";
    public bool AutoDiscoveryEnabled { get; }

    public string HomeAssistantPlatform => Writable ? "switch" : "binary_sensor";
    public string HomeAssistantDeviceClass
    {
        get
        {
            if (Writable)
            {
                return null;
            }

            switch (reg.Unit)
            {
                case RegUnit.HeatOnOff:
                    return "heat";
                case RegUnit.Problem:
                    return "problem";
                case RegUnit.OverpressureOnOff:
                    return null;
                case RegUnit.Presence:
                    return "connectivity";
                default:
                    return "running";
            }
        }
    }

    public string HomeAssistantUnitOfMeasurement => null;
    public bool Writable => reg.Writable;
    public int Min => 0;
    public int Max => 1;
}