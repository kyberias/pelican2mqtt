using System;
using pelican2mqtt.Pelican;

namespace pelican2mqtt;

interface IMqttRegister
{
    string Topic { get; }
    string Value { get; }
    event EventHandler ValueChanged;
    RegUnit Unit { get; }

    /// <summary>
    /// Object id for Homeassistant MQTT discovery:
    /// The ID of the device. This is only to allow for separate topics for each
    /// device and is not used for the entity_id. The ID of the device
    /// must only consist of characters from the character
    /// class [a-zA-Z0-9_-] (alphanumerics, underscore and hyphen).
    /// </summary>
    string ObjectId { get; }

    bool AutoDiscoveryEnabled { get; }

    string HomeAssistantPlatform { get; }

    string HomeAssistantDeviceClass { get; }
    string HomeAssistantUnitOfMeasurement { get; }
}
