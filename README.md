# stac2mqtt

Bridge [SmartThings.com](https://www.smartthings.com) airconditioners to MQTT without a reverse proxy. [Home Assistant](https://www.home-assistant.io) [MQTT integration](https://www.home-assistant.io/integrations/mqtt) automatically recognises the devices.

Using Home Assistant UI, one can monitor the ambient temperature, humidity and control/automate multiple units.

Currently only supports Samsung Geo+ models.

## Configuration 

The application can be configured through:

1. A `data/settings.json` file with structured device configurations:
   ```json
   {
     "MqttServer": "10.10.10.11",
     "Devices": [
       {
         "DeviceId": "11111111-2222-3333-4444-555555555555",
         "Name": "Living Room AC",
         "Area": "living_room"
       },
       {
         "DeviceId": "22222222-2222-3333-4444-555555555555",
         "Name": "Bedroom AC",
         "Area": "bedroom"
       }
     ],
     "SmartThings": {
       "ApiToken": "66666666-7777-8888-9999-000000000000",
       "RefreshToken": "refresh-token-value",
       "ClientId": "client-id-value",
       "ClientSecret": "client-secret-value"
     }
   }
   ```

2. Environment variables (with nested structures supported):
   ```bash
   docker run --detach \
     --env MqttServer=10.10.10.11 \
     --env Devices__0__DeviceId=11111111-2222-3333-4444-555555555555 \
     --env Devices__0__Name="Living Room AC" \
     --env Devices__0__Area=living_room \
     --env Devices__1__DeviceId=22222222-2222-3333-4444-555555555555 \
     --env Devices__1__Name="Bedroom AC" \
     --env Devices__1__Area=bedroom \
     --env SmartThings__ApiToken=66666666-7777-8888-9999-000000000000 \
     --env SmartThings__RefreshToken=refresh-token-value \
     --name stac2mqtt mybura/stac2mqtt:latest
   ```

## Usage

To be run as a docker instance (tested on Linux host and Mac OS with Docker (see docker-compose.yml), Windows should work if you build a Windows image using this repo)

### Required:

1. SmartThings account:
   - Personal access token (if your Personal Access Token was created before 1st January 2025)
   - Refresh token, clientId and clientSecret (if your Personal Access Token was created after 1st January 2025) - [more information on how to generate client_id and client_secret](https://github.com/SmartThingsCommunity/api-app-subscription-example-js/tree/master)
   - After you successfully get your clientId and clientSecret, type this in your browser: https://api.smartthings.com/oauth/authorize?client_id={{client_id}}&response_type=code&redirect_uri={{redirect_uri}}. Make sure redirect_uri is the same as the one you provided when you created your OAuth app. It does not need to work. After you finish the authorization, there will be a code in the URL. Copy that code to use in the next step.
   - With the code, redirect_uri, clientId and clientSecret you can fetch the access token and refresh token with a POST to https://auth-global.api.smartthings.com/oauth/token?grant_type=authorization_code&redirect_uri={{redirect_uri}}&code={{code}}. Don't forget to use Base Auth with your clientId and clientSecret. I recommend using Postman to do this.
   - Add the access token and refresh token to the data/smartthings_tokens.json file.
2. DeviceId(s) of the devices to control
3. Server address of an MQTT broker (e.g. [mosquitto](https://mosquitto.org/)) requiring no credentials
4. [Working Docker host](https://www.tutorialspoint.com/docker/docker_installation.htm)

### Example:

- SmartThings personal access token = 66666666-7777-8888-9999-000000000000
- MQTT server IP = 10.10.10.11
- DeviceId = 11111111-2222-3333-4444-555555555555
- Desired docker instance name = stac2mqtt

```bash
docker run --detach --env MqttServer=10.10.10.11 --env DeviceIds__0=11111111-2222-3333-4444-555555555555 --env SmartThings__ApiToken=66666666-7777-8888-9999-000000000000 --env SmartThings__RefreshToken=initial-refresh-token --env SmartThings__ClientCredentials=base64-encoded-credentials --name stac2mqtt mybura/stac2mqtt:latest
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
docker run --detach --env MqttServer=10.10.10.11 --env DeviceIds__0=aaaaaaaa-2222-3333-4444-555555555555 --env DeviceIds__1=bbbbbbbb-2222-3333-4444-555555555555 --env SmartThings__ApiToken=66666666-7777-8888-9999-000000000000 --env SmartThings__RefreshToken=initial-refresh-token --env SmartThings__ClientCredentials=base64-encoded-credentials --name stac2mqtt mybura/stac2mqtt:latest
```

## Notes

- The code can be run as an executable. Just download this repo and build/run it from Visual Studio 2022.
- Additional settings are available, see the Configuration class.
- Supports Settings.json instead of environment variable configuration.
- Token persistence: Access and refresh tokens are automatically stored and managed by the application.
- Why not just use [Home Assistant Smartthings Integration](https://www.home-assistant.io/integrations/smartthings/)? More maintenance and higher security risk needed in setting up a reverse proxy.
- Once a local network only mod is created for the Samsung Wifi control modules, it will be easy to modify this bridge and keep automations/config in Home Assistant the same. Gets you off the hostile internet and less migration work.
- Other devices can be added by creating extra drivers. An example is in the Drivers/SamsungGeoPlus folder. Some exploration of supported SmartThings attributes and behaviours are needed. The framework automatically detects new drivers by checking for classes that implement IDriver.
- Use https://my.smartthings.com/advanced/devices to check if devices are registered and are responding to commands.

## License

The MIT License (MIT)