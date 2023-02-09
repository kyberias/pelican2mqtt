namespace pelican2mqtt.Pelican;

static class EnerventProtocolSender
{
    public static byte[] SetFanSpeeds(byte supplyFan, byte exhaustFan)
    {
        var buf = new byte[6];
        buf[0] = 0x80;
        buf[1] = 0x01;
        buf[2] = 0x01;
        buf[3] = 0;
        buf[4] = 60;
        buf[5] = supplyFan;
        buf[6] = exhaustFan;

        byte len = 9;
        buf[3] = len;
        byte sum = (byte)(0x01 + 0x01 + len);
        for (int i = 0; i < 3; i++)
        {
            sum += buf[4 + i];
        }

        buf[7] = sum;
        buf[8] = 0x81;

        return buf;
    }

    public static byte[] SetHeaterCoolerState(bool on)
    {
        var buf = new byte[6];
        buf[0] = 0x80;
        buf[1] = 0x01;
        buf[2] = 0x01;
        buf[3] = 0;
        buf[4] = 60;
        buf[5] = on ? (byte)1 : (byte)0;

        byte len = 8;
        buf[3] = len;
        byte sum = (byte)(0x01 + 0x01 + len);
        for (int i = 0; i < 2; i++)
        {
            sum += buf[4 + i];
        }

        buf[6] = sum;
        buf[7] = 0x81;

        return buf;
    }
}