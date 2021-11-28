using System;

namespace ModbusDump
{
    interface IRegister
    {
        string Address { get; }
        string? Value { get; }
        event EventHandler ValueChanged;
    }

    class Register : IRegister
    {
        public Register(byte address, byte index, string name, RegAccess access, RegUnit unit)
        {
            Address = address;
            Index = index;
            Name = name;
            Access = access;
            Unit = unit;
        }

        public byte Address { get; }

        private static int ConvertTwosComplementByteToInteger(byte rawValue)
        {
            // If a positive value, return it
            if ((rawValue & 0x80) == 0)
            {
                return rawValue;
            }

            // Otherwise perform the 2's complement math on the value
            return (byte)(~(rawValue - 0x01)) * -1;
        }

        public string? Value
        {
            get
            {
                switch (Unit)
                {
                    case RegUnit.Celsius:
                        int temperature = ConvertTwosComplementByteToInteger(Data.GetValueOrDefault());
                        return temperature.ToString();
                    case RegUnit.Percentage:
                    case RegUnit.VentilationSpeed:
                        return Data.GetValueOrDefault().ToString();
                    case RegUnit.PercentageOfMaximum:
                        return ((int)(Data.GetValueOrDefault() / 255.0 * 100)).ToString();
                    default:
                        throw new NotSupportedException(Unit.ToString());
                }
            }
        }

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

        public string Name { get; }

        public RegAccess Access { get; }
        public RegUnit Unit { get; }

        string IRegister.Address => Name;

        public event EventHandler ValueChanged = delegate { };
    }
}