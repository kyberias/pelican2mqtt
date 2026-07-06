using pelican2mqtt.Pelican;

namespace pelican2mqtt;

class RegisterConfigCommon
{
    public string name { get; set; }
    public string topic { get; set; }
    public bool autoDiscovery { get; set; } = false;
    public RegUnit type { get; set; }
    public bool writable { get; set; } = false;
}

class RegisterConfig : RegisterConfigCommon
{
    public int address { get; set; }
    public int index { get; set; }
    public BitRegisterConfig[] bits { get; set; }
}