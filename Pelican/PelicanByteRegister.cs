using System;

namespace pelican2mqtt.Pelican
{
    enum RegAccess
    {
        ReadOnly,
        ReadWrite
    };

    enum RegUnit
    {
        Unknown,
        Celsius, // two's complement byte
        Pascal,
        Minutes,
        VentilationSpeed, // 1 - 6
        Percentage, // 100 = 100%
        PercentageOfMaximum, // 0xFF = 100%,
        Index,
        WeekdaysBitfield,
        BitField,
        RelativeHumidity,
        HeatOnOff,
        HrwOnOff,
        // value = hours * 8 + 1 * (each 15 minutes past the hours). E.g. 18.30 = 18 * 8 + 2 = 146 (0x92)
        Time,
        OnOff
    }

    /*interface IRegister
    {
        string Address { get; }
        string? Value { get; }
    }*/

    class PelicanByteRegister : IByteRegister
    {
        public PelicanByteRegister(byte address, byte index, RegAccess access, RegUnit unit)
        {
            Address = address;
            Index = index;
            Access = access;
            Unit = unit;
        }

        public byte Address { get; }

        public byte Index { get; }

        private byte? data;

        public byte? Data
        {
            get => data;
            set
            {
                var oldValue = data;
                data = value;
                if (oldValue != data)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public void ClearOldValue()
        {
            data = null;
        }

        public RegAccess Access { get; }
        public RegUnit Unit { get; }

        public event EventHandler ValueChanged = delegate { };
    }
}