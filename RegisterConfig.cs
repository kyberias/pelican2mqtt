using pelican2mqtt.Pelican;

namespace pelican2mqtt;

class RegisterConfig
{
    public int address { get; set; }
    public int index { get; set; }
    public RegUnit type { get; set; }
    public string name { get; set; }
    public string topic { get; set; }
    public BitRegisterConfig[] bits { get; set; }
}