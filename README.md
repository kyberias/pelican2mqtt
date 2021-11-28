# pelican2mqtt

This program reads serial port communication from Enervent Pelican ACE ventilation machine and publishes the information to MQTT broker.

## Build

TBD

## Installation

## Usage

Example command line:

pelican2mqtt -m mqttBrokerHostname -l mqttUsername -w mqttPassword -i COM3

### Command line settings

Run the tool with --help parameter to get a summary of the settings.

|Setting||
|---|---|
|-s, --server|Instead of publishing the information to MQTT broker, create a TCP server to publish the raw stream of bytes from the serial port. (Useful for debugging)
|-c, --client|Instead of reading the bytes from a serial port, connect to a TCP server to get the stream of bytes (Useful for debugging)
|-h, --host|Host name to use with -c parameter.
|-p, --port|TCP port to connect or listen. (Default 8990)
|-i, --serialport|Name of the serial port to use. E.g. COM3
|-m, --mqttserver|Host name or IP-address of the MQTT broker to connect to (e.g. homeassistant). Default port 1883 is used.
|-l, --mqttlogin|Username to use when connecting to MQTT broker
|-w, --mqttpassword|Password to use when connecting to MQTT broker


## Integration with Home Assistant

## Principle of operation

Enervent Pelican ACE uses a RS-485 serial bus in communicating with remote display and control units. Pelican broadcasts internal settings and statuses through the bus and receives commands and settings from the control units.

## MQTT

The following image shows the information published to the MQTT broker.

![MQTT topics published](MQTT.png "MQTT")


### Serial comm settings

Bit rate is 19200 bps, 8 data bits, no parity and one stop bit (8N1).


### Protocol details

Messages in the protocol have the following frame format:

|Byte|Hex value|Description|
|---|---|--|
|STX|0x80|Start byte
|SRC|0x01|Source address, always 1
|DST|0x01|Destination address, always 1
|LEN||Length of the message in bytes
|||Data bytes
|CRC||Simple checksum
|ETX|0x81|End byte

### Broadcast messages

|Command|Index|Description|
|---|---|--|
|E6|1|Supply air temperature|
||2|Exhaust air temperature|
||3|Waste air temperature|
||4|Outside temperature|
||5|Return water temperature|
||6|Supply air temperature just after HRW, but before heating element.|
|E7|1|Supply air temperature preset value|
||2|Exhaust air temperature preset value|
||3|Room temperature preset value|
||4|Low limit for supply air temperature|
||5|High limit for supply air temperature|
|E8|1|Supply fan normal speed setting|
||2|Exhaust fan normal speed setting|
||3|Supply fan speed setting in timer program|
||4|Exhaust fan speed setting in timer program|
||5|Supply fan speed setting when overpressurizing|
||6|Exhaust fan speed setting when overpressurizing|
