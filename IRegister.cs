using System;

namespace pelican2mqtt;

interface IRegister
{
    event EventHandler ValueChanged;
}