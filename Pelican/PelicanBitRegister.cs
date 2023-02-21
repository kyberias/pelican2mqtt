using System;

namespace pelican2mqtt.Pelican
{
    class PelicanBitRegister : IBitRegister
    {
        private readonly PelicanByteRegister reg;
        private readonly byte bit;
        private bool writable;

        public PelicanBitRegister(PelicanByteRegister reg, byte bit, RegUnit unit, bool writable)
        {
            this.reg = reg;
            this.bit = bit;
            Unit = unit;
            this.writable = writable;

            reg.ValueChanged += (sender, args) =>
            {
                ValueChanged(this, EventArgs.Empty);
            };
        }

        public byte Address => reg.Address;
        public byte Index => reg.Index;

        byte IBitRegister.Bit => bit;
        
        public RegUnit Unit { get; }

        public bool? On
        {
            get
            {
                if (!reg.Data.HasValue)
                {
                    return null;
                }

                return (reg.Data.Value & 1 << bit) > 0;
            }
        }

        public event EventHandler ValueChanged = delegate { };
        public bool Writable => writable;
    }
}