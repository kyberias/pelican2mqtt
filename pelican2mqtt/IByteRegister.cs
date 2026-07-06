using pelican2mqtt.Pelican;

namespace pelican2mqtt;

interface IByteRegister : IRegister
{
    byte? Data { get; set; }
    RegUnit Unit { get; }
    byte Address { get; }
    byte Index { get; }
    void ClearOldValue();
}