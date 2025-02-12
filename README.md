# stac2mqtt

Bridge [SmartThings.com](https://www.smartthings.com) airconditioners to MQTT without a reverse proxy. [Home Assistant](https://www.home-assistant.io) [MQTT integration](https://www.home-assistant.io/integrations/mqtt) automatically recognises the devices.

Using Home Assistant UI, one can monitor the ambient temperature, humidity and control/automate multiple units.

Currently only supports Samsung Geo+ models.

## Usage

To be run as a docker instance (tested on Linux host, Windows should work if you build a Windows image using this repo)

### Required:

1. SmartThings account and [personal access token](https://developer.smartthings.com/docs/advanced/authorization-and-permissions/) 
2. DeviceId(s) of the devices to control
3. Server address of an MQTT broker (e.g. [mosquitto](https://mosquitto.org/)) requiring no credentials
4. [Working Docker host](https://www.tutorialspoint.com/docker/docker_installation.htm)

### Example:

- SmartThings personal access token = 66666666-7777-8888-9999-000000000000
- MQTT server IP = 10.10.10.11
- DeviceId = 11111111-2222-3333-4444-555555555555
- Desired docker instance name = stac2mqtt

```bash
docker run --detach --env MqttServer=10.10.10.11 --env DeviceIds__0=11111111-2222-3333-4444-555555555555 --env SmartThings__ApiToken=66666666-7777-8888-9999-000000000000 --name stac2mqtt mybura/stac2mqtt:latest
```

## More Examples

### Example (2 devices):

For each additional device, just add 1 to the DeviceIds__xxxx environment variable passed via docker command line.

- SmartThings personal access token = 66666666-7777-8888-9999-000000000000
- MQTT server IP = 10.10.10.11
- First DeviceId = aaaaaaaa-2222-3333-4444-555555555555
- Second DeviceId = bbbbbbbb-2222-3333-4444-555555555555
- Desired docker instance name = stac2mqtt

```bash
docker run --detach --env MqttServer=10.10.10.11 --env DeviceIds__0=aaaaaaaa-2222-3333-4444-555555555555 --env DeviceIds__1=bbbbbbbb-2222-3333-4444-555555555555 --env SmartThings__ApiToken=66666666-7777-8888-9999-000000000000 --name stac2mqtt mybura/stac2mqtt:latest
```

## Notes

- The code can be run as an executable. Just download this repo and build/run it from Visual Studio 2022.
- Additional settings are available, see the Configuration class.
- Supports Settings.json instead of environment variable configuration.
- Why not just use [Home Assistant Smartthings Integration](https://www.home-assistant.io/integrations/smartthings/)? More maintenance and higher security risk needed in setting up a reverse proxy.
- Once a local network only mod is created for the Samsung Wifi control modules, it will be easy to modify this bridge and keep automations/config in Home Assistant the same. Gets you off the hostile internet and less migration work.
- Other devices can be added by creating extra drivers. An example is in the Drivers/SamsungGeoPlus folder. Some exploration of supported SmartThings attributes and behaviours are needed. The framework automatically detects new drivers by checking for classes that implement IDriver.
- Use https://my.smartthings.com/advanced/devices to check if devices are registered and are responding to commands.

## License

The MIT License (MIT)