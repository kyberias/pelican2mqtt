using System;

namespace pelican2mqtt;

interface IMqttRegister
{
    string Address { get; }
    string Value { get; }
    event EventHandler ValueChanged;
}