using System;

namespace pelican2mqtt.Pelican;

class MqttBitRegister : IMqttRegister
{
    private readonly IBitRegister reg;

    public MqttBitRegister(string address, IBitRegister reg)
    {
        this.reg = reg;
        reg.ValueChanged += Reg_ValueChanged;
        Address = address;
    }

    private void Reg_ValueChanged(object sender, EventArgs e)
    {
        ValueChanged.Invoke(this, EventArgs.Empty);
    }

    public string Address { get; }
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
}