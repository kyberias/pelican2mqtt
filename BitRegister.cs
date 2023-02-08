using System;
using pelican2mqtt;

namespace ModbusDump
{
    class BitRegister : IRegister
    {
        private readonly Register reg;

        public BitRegister(Register reg, int bit, string address)
        {
            this.reg = reg;
            Address = address;
            Bit = bit;

            reg.ValueChanged += (sender, args) =>
            {
                ValueChanged(this, EventArgs.Empty);
            };
        }

        public int Bit { get; }

        public bool? On
        {
            get
            {
                if (!reg.Data.HasValue)
                {
                    return null;
                }

                return (reg.Data.Value & (1 << Bit)) > 0;
            }
        }

        public string Address { get; }

        public string? Value
        {
            get
            {
                if (On == null)
                {
                    return null;
                }

                return On == true ? "on" : "off";
            }
        }

        public event EventHandler ValueChanged = delegate {};
    }
}