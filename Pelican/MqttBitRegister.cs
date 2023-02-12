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

            return reg.On == true ? "on" : "off";
        }
    }
    public event EventHandler ValueChanged = delegate { };
    public RegUnit Unit => RegUnit.OnOff;
    public string ObjectId => $"register_{reg.Address:X2_reg.}_{reg.Index}_{reg.Bit}";
    public bool AutoDiscoveryEnabled { get; }

    public string HomeAssistantPlatform => "binary_sensor";
    public string HomeAssistantDeviceClass => reg.Unit == RegUnit.HeatOnOff ? "heat" : "running";
    public string HomeAssistantUnitOfMeasurement => "";
}