using System;

namespace pelican2mqtt.Pelican
{
    class PelicanBitRegister : IBitRegister
    {
        private readonly PelicanByteRegister reg;

        public PelicanBitRegister(PelicanByteRegister reg, int bit)
        {
            this.reg = reg;
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

                return (reg.Data.Value & 1 << Bit) > 0;
            }
        }

        public event EventHandler ValueChanged = delegate { };
    }
}