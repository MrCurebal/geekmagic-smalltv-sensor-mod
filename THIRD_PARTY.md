# Third-party components

| Component | Use | Upstream license |
|---|---|---|
| [giovi321/smalltv-mod](https://github.com/giovi321/smalltv-mod) | `firmware/src/loader.cpp`, adapted to include the local configuration header | WTFPL; original attribution retained |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 | Windows sensor readings, consumed as a NuGet dependency | MPL-2.0 and upstream third-party notices |
| [ESP8266 Arduino core](https://github.com/esp8266/Arduino) | Wi-Fi, HTTP, OTA, SPI and EEPROM support | See upstream licenses and SDK terms |
| [Adafruit GFX](https://github.com/adafruit/Adafruit-GFX-Library) | Graphics primitives and font support | BSD; individual bundled fonts retain their own notices |
| [Adafruit ST7735/ST7789](https://github.com/adafruit/Adafruit-ST7735-Library) 1.11.0 | ST7789 display driver | BSD |
| [ArduinoJson](https://github.com/bblanchon/ArduinoJson) 7.4.3 | JSON parsing and responses | MIT |

Dependencies are resolved by PlatformIO/NuGet; their compiled libraries and fonts are not vendored into this source repository. Preserve upstream notices when distributing builds. PawnIO is an external sensor-access prerequisite and is not redistributed here.
