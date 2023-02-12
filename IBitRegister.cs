using pelican2mqtt.Pelican;

namespace pelican2mqtt;

interface IBitRegister : IRegister
{
    bool? On { get; }
    byte Address { get; }
    byte Index { get; }
    byte Bit { get; }
    RegUnit Unit { get; }
}