# NextDNS DoH (DNS-over-HTTPS)

A small Windows tray app that turns [NextDNS](https://nextdns.io) DNS-over-HTTPS [on](/screenshots/systray_nextdns_on.png) or [off](/screenshots/systray_nextdns_off.png) for the active Wi-Fi and Ethernet adapters.

It lives in the notification area. Left-click the icon (or use **NextDNS on/off** in the menu) to toggle. Changing DNS requires Administrator access; Windows will show a UAC prompt when needed. 

![](/screenshots/config.png)

## What it does

- Sets NextDNS as the DNS-over-HTTPS resolver using your configuration ID from [my.nextdns.io](https://my.nextdns.io)
- Identifies this PC in NextDNS logs with a device ID, name, and model (same idea as the [NextDNS CLI](https://github.com/nextdns/nextdns))
- Applies the change to active Ethernet and Wi-Fi adapters (not VPN, Bluetooth, or Hyper-V virtual switches)
- Restores DHCP DNS when you turn it off
- Can start with Windows
- Stores your configuration ID and device name in `%AppData%\nextdns-doh\settings.json`

On first run the app asks for your NextDNS configuration ID. That ID is the path segment in `https://dns.nextdns.io/[ID]`.

**Requirements:** Windows 11 x64 with [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included on current Windows 11).

## Download

[NextDNS-DoH-1.0.4.exe](https://github.com/R0GGER/NextDNS-DoH/releases/download/1.0.4/NextDNS-DoH-1.0.4.exe) — Windows installer. No Administrator rights needed to install; UAC is only requested when you toggle DNS.

## Build outputs


| File                         | What it is                                                                                                       |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `publish/nextdns-doh.exe`    | Portable app. Run it as-is; no installer.                                                                        |
| `dist/NextDNS-DoH-1.0.4.exe` | Setup that copies the app to `%LocalAppData%\Programs\NextDNS DoH`, adds a Start Menu shortcut, and launches it. |


The version in the installer filename comes from `<Version>` in `nextdns-doh.csproj` (currently `1.0.4`).

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

Double-click the setup (or run it silently with `/VERYSILENT`). It does not require Administrator rights to install; UAC is only requested later when you toggle DNS.

Uninstall from **Settings → Apps**, or run the uninstaller from the Start Menu folder. Uninstall also removes the “Start with Windows” registry value.

## Usage

1. Run `nextdns-doh.exe` or the installer.
2. Enter your NextDNS configuration ID and optionally a device name (defaults to this PC’s name).
3. Optionally enable NextDNS immediately.
4. Left-click the tray icon to toggle [on](/screenshots/systray_nextdns_on.png) or [off](/screenshots/systray_nextdns_off.png), or [right-click](/screenshots/config.png) for **Settings**, **Start with Windows**, and **Exit**.

