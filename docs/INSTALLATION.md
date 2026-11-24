# Wi-Fi installation

## Before starting

Confirm that the stock device reports `SmallTV-Ultra` at `/v.json`. These builds target an ESP8266, not a SmallTV Pro or ESP32 clone. Flashing replaces the stock application. A failed flash, incompatible board or broken network setup can require opening the case and using a 3.3 V UART adapter. A stock `.bin` file alone may not restore all factory resources.

The two-stage route below has been exercised from Ultra-V9.0.41 on one device. This is not a guarantee for every product sold under the same name. Use stable power and a reliable 2.4 GHz Wi-Fi connection. Do not unplug during an update.

## Prepare local files

Run `python tools/configure.py` from the repository root. It creates `firmware/include/private_config.h` and matching sender configuration files. Your Wi-Fi password and a generated random key are compiled into your private firmware build. Keep those binaries private too.

Build with:

```sh
pio run -d firmware -e loader -e monitor
```

Expected output paths:

- `firmware/.pio/build/loader/firmware.bin`: the small first-stage loader.
- `firmware/.pio/build/monitor/firmware.bin`: the native monitor application.

## Stage 1: stock firmware to loader

1. Open `http://DEVICE_IP/update`.
2. Select the **loader** binary in the **Firmware** field, not FileSystem.
3. Upload once and wait for the success message and reboot.
4. The loader tries the Wi-Fi credentials from your private header. If it cannot join, connect your computer or phone to its open `SmallTV-Loader` network and open `http://192.168.4.1/update`.

A dark screen during this loader stage does not by itself mean the clock is broken. Test its Wi-Fi access point and update page.

## Stage 2: loader to monitor

1. Open the loader's `/update` page.
2. Upload the **monitor** binary in the Firmware field.
3. Wait for reboot. The display joins your configured network; DHCP may give it a new address.
4. Visit `/health` at its new address. It should report `pc-info-native-1.5-en`.
5. Update the `Device` URL in each local sender's `config.json` if necessary.

Later firmware uploads use the monitor's password-protected `/update` page directly. Username: `admin`. Password: the generated `ApiKey` in your local configuration.

## Display and network settings

The web page offers brightness, rotation, inversion and `/network` settings. Display choices are persisted in EEPROM only when settings change; live sensor updates remain in RAM. The shipped defaults are 32% brightness and 180° rotation. Adjust them for your installation.

If the device cannot connect for 45 seconds, it opens `PC-Info-Recovery`. Its Wi-Fi password is your generated API key. Connect, open `http://192.168.4.1/network`, log in as `admin` using the same key, and enter the correct network details. The application keeps retrying station mode.

## Windows startup

Build both Windows projects into `dist/windows`, keep the generated `config.json` there, and run the startup installer as administrator. It creates `GeekMagicSensorSender` for the current user's interactive logon with elevated sensor access. It does not store a Windows password. The control app itself does not require elevation.

The sender's `monitor.log` records connection results. `readings.json` contains the most recent measurements. Both are ignored by Git. Unknown sensors remain null; no placeholder temperatures are substituted.
