# HTTP API

All routes are on your device's local HTTP address.

| Route | Authentication | Purpose |
|---|---|---|
| `GET /health` | None | Firmware version, uptime, heap, frame count, freshness, brightness and renderer information |
| `POST /api/metrics` | `X-Api-Key` | Update the RAM-resident sensor values |
| `GET /api/metrics` | `X-Api-Key` or HTTP Basic | Read the current sensor values |
| `GET/POST /api/layout` | `X-Api-Key` or HTTP Basic | Read/save panel count, arrangement and selected metrics |
| `GET /` | HTTP Basic | Display settings page |
| `POST /display` | HTTP Basic | Apply display settings |
| `GET/POST /network` | HTTP Basic | View/save Wi-Fi configuration |
| `GET/POST /update` | HTTP Basic | Firmware update form and upload |

HTTP Basic uses username `admin` and your configured API key as the password.

## Metrics

Send a JSON object with these fields. Numbers are degrees Celsius, percentages or GiB as appropriate. Use `null` for unavailable sensors.

```json
{
  "CaseTemp": 30,
  "CpuTemp": 45,
  "CpuLoad": 12,
  "GpuTemp": 48,
  "GpuLoad": 8,
  "RamLoad": 40,
  "RamUsed": 12.8,
  "RamTotal": 32
}
```

An accepted update returns `{"ok":true,"storage":"ram"}`. CPU/GPU/case temperatures must be above 0 and at most 125; loads must be between 0 and 100. Invalid or absent individual measurements appear as unavailable. Invalid JSON returns 400, a missing/incorrect key returns 401, and a payload longer than 2048 characters is rejected with 413.

The display marks values stale after 15 seconds without a new accepted packet. It retains the last numbers and changes the captions to `WAITING FOR DATA`.

## Display settings

POST URL-encoded form fields to `/display`: `brightness` (5–100), `rotation` (0–3), and/or `inversion` (`0` or `1`). Only supplied fields change. A successful application returns 303 to `/`. Explicit setting changes are saved; repeating the same settings does not commit another EEPROM write.

## Updates

POST a multipart form to `/update` using the `firmware` field. Only upload an ESP8266 monitor build prepared for this hardware. Never upload a filesystem image into the firmware field or an ESP32 build.

## Custom panel layouts (firmware 1.5+)

POST a JSON object to `/api/layout`:

```json
{
  "count": 2,
  "orientation": "stacked",
  "metrics": ["cpu_temp", "gpu_load"]
}
```

`count` is 1–4. `orientation` is `side-by-side` (two columns) or `stacked` (two rows), used when count is two. Count three uses two top panels and one wide bottom panel. Count four uses a 2 × 2 grid. The metrics array must have exactly `count` entries. Valid IDs are `case_temp`, `cpu_temp`, `cpu_load`, `gpu_temp`, `gpu_load`, `ram_load`, `ram_used` and `ram_total`. Duplicate selections are allowed. Unknown IDs, invalid counts, and invalid arrangements return 400 without changing the active layout. Payloads over 512 characters return 413. Missing/wrong authentication returns 401.

GET and successful POST return the active layout in the same JSON format. Layout changes are saved to EEPROM once per explicit change, not on each sensor update. Existing brightness/rotation settings from firmware 1.4 are preserved.
