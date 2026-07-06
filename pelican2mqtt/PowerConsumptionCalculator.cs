using System;
using System.Globalization;
using pelican2mqtt.Pelican;

namespace pelican2mqtt;

class PowerConsumptionCalculator : IMqttRegister
{
    private readonly PelicanByteRegister supplyFanSpeed;
    private readonly PelicanByteRegister exhaustFanSpeed;

    public PowerConsumptionCalculator(PelicanByteRegister supplyFanSpeed, PelicanByteRegister exhaustFanSpeed)
    {
        this.supplyFanSpeed = supplyFanSpeed;
        this.exhaustFanSpeed = exhaustFanSpeed;

        this.supplyFanSpeed.ValueChanged += FanSpeed_ValueChanged;
        this.exhaustFanSpeed.ValueChanged += FanSpeed_ValueChanged;
    }

    private void FanSpeed_ValueChanged(object sender, EventArgs e)
    {
        if (supplyFanSpeed.Data.HasValue && exhaustFanSpeed.Data.HasValue)
        {
            currentPower = 22.125 * (supplyFanSpeed.Data.Value + exhaustFanSpeed.Data.Value) + 76;
            ValueChanged.Invoke(this, EventArgs.Empty);
        }
    }

    private double currentPower;

    public string Topic => "status/powerConsumption";
    public string Value => currentPower.ToString(CultureInfo.InvariantCulture);

    public event EventHandler ValueChanged = delegate {};

    public RegUnit Unit => RegUnit.Watts;

    public string ObjectId => "PowerConsumption";

    public bool AutoDiscoveryEnabled => true;
    public string HomeAssistantPlatform => "sensor";
    public string HomeAssistantDeviceClass => "power";
    public string HomeAssistantUnitOfMeasurement => "W";

    public bool Writable => false;
    public int Min => 0;
    public int Max => 600;
}