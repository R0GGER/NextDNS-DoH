# NextDNS DoH (DNS-over-HTTPS)

A small Windows tray app that turns [NextDNS](https://nextdns.io) DNS-over-HTTPS [on](/screenshots/systray_nextdns_on.png) or [off](/screenshots/systray_nextdns_off.png) for the active Wi-Fi and Ethernet adapters.

It lives in the notification area. Left-click the icon (or use **NextDNS on/off** in the menu) to toggle. Changing DNS requires Administrator access, but the installer asks for it once and registers two scheduled tasks, so toggling and changing settings afterwards no longer show a UAC prompt.

![](/screenshots/config.png)

## What it does

- Sets NextDNS as the DNS-over-HTTPS resolver using your configuration ID from [my.nextdns.io](https://my.nextdns.io)
- Identifies this PC in NextDNS logs with a device ID, name, and model (same idea as the [NextDNS CLI](https://github.com/nextdns/nextdns))
- Applies the change to active Ethernet and Wi-Fi adapters (not VPN, Bluetooth, or Hyper-V virtual switches)
- Restores DHCP DNS when you turn it off
- Can start with Windows
- Stores your configuration ID and device name in `%AppData%\nextdns-doh\settings.json`
- Toggles without a UAC prompt through the **NextDNS DoH\Apply On** and **Apply Off** scheduled tasks that the installer registers

On first run the app asks for your NextDNS configuration ID. That ID is the path segment in `https://dns.nextdns.io/[ID]`.

**Requirements:** Windows 11 x64 with [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included on current Windows 11).

## Download

[NextDNS-DoH-1.0.6.exe](https://github.com/R0GGER/NextDNS-DoH/releases/download/1.0.6/NextDNS-DoH-1.0.6.exe) — Windows installer. It asks for Administrator rights once and installs to `%ProgramFiles%\NextDNS DoH`. After that, turning NextDNS on or off no longer prompts.

## Install & Run

1. Run the installer and approve the UAC prompt. It installs the app, registers the scheduled tasks that do the elevated work, and starts the tray app.
2. Enter your NextDNS configuration ID and optionally a device name (defaults to this PC’s name).
3. Optionally enable NextDNS immediately.
4. Left-click the tray icon to toggle [on](/screenshots/systray_nextdns_on.png) or [off](/screenshots/systray_nextdns_off.png), or [right-click](/screenshots/config.png) for **Settings**, **Start with Windows**, and **Exit**.

Two situations still fall back to a UAC prompt per change:

- Running `nextdns-doh.exe` portable, without the installer. There are no scheduled tasks then.
- Using the app from a second Windows account. The tasks are registered for the account that ran the installer.

## Troubleshooting

### Some sites do not open when NextDNS DoH is on

**Symptom:** with NextDNS DoH on, a browser shows **Unable to connect** for some sites (often Cloudflare-hosted), even though the [NextDNS logs](https://my.nextdns.io) show the domain was not blocked. Turning NextDNS off makes those sites work again.

**Cause:** your internet connection has no working IPv6, but DoH still returns IPv6 addresses for those sites. The browser tries IPv6 first and the connection times out instead of falling back to IPv4.

**Fix 1 — turn off IPv6 on the network adapter (recommended).** This fixes it for every browser and app at once. Open **Control Panel → Network and Sharing Center → Change adapter settings**, right-click your Wi-Fi or Ethernet adapter, choose **Properties**, clear the checkbox for **Internet Protocol Version 6 (TCP/IPv6)**, and click **OK**. Do this for each adapter you actually use. Only do this if your provider does not give you IPv6; if it does, leave it enabled.

**Fix 2 — Firefox only.** If you would rather not change the adapter, open `about:config` in Firefox and set both preferences below, then restart Firefox. This only affects Firefox; other browsers keep the problem.

| Preference | Value |
| --- | --- |
| `network.dns.native_https_query` | `false` |
| `network.dns.disableIPv6` | `true` |

## Uninstall

Uninstall from **Settings → Apps**, or run the uninstaller from the Start Menu folder. Uninstall also removes the “Start with Windows” registry value.

## Build outputs


| File                         | What it is                                                                                                       |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `publish/nextdns-doh.exe`    | Portable app. Run it as-is; no installer.                                                                        |
| `dist/NextDNS-DoH-1.0.6.exe` | Setup that copies the app to `%ProgramFiles%\NextDNS DoH`, registers the scheduled tasks, adds a Start Menu shortcut, and launches it. |


The version in the installer filename comes from `<Version>` in `nextdns-doh.csproj` (currently `1.0.6`).

## Prerequisites

To build you need:

- [.NET SDK](https://dotnet.microsoft.com/download) (8 or later is fine; the app itself targets .NET Framework 4.8)
- Windows x64

The installer build also needs [Inno Setup 6](https://jrsoftware.org/isinfo.php). If `ISCC.exe` is not already installed, `build-installer.ps1` downloads the compiler automatically.

## Build the portable app

From the project root:

```powershell
dotnet publish nextdns-doh.csproj -c Release -o publish --nologo
```

That writes:

- `publish/nextdns-doh.exe`
- `publish/nextdns-doh.exe.config`

You can copy those two files anywhere and run `nextdns-doh.exe`.

## Build the installer

From the project root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The script:

1. Publishes the app to `publish/`
2. Compiles `installer/nextdns-doh.iss` with Inno Setup
3. Writes `dist/NextDNS-DoH-<version>.exe`

Double-click the setup (or run it silently with `/VERYSILENT`). It requires Administrator rights, because it installs to `Program Files` and registers the scheduled tasks that make later toggles prompt-free.