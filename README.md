# AOG Bogballe Bridge
## Section control for Bogballe spreaders

The Bridge listens to the speed, section and machine configuration PGNs broadcast by AgOpenGPS, and sends them to the spreader over RS232 using the [Bogballe serial protocol](https://dam.bogballe.com/dmm3bwsv3/AssetStream.aspx?mediaformatid=10061&destinationid=10016&assetid=3488).

This should work with all 3 Bogballe spreader controllers: TOTZ, ZURF and UNIQ. If you have any compatibility issues, please let me know.

## Instructions for use

You will need a USB -> RS232 adapter and null modem cable to connect to the port on TOTZ/ZURF/UNIQ.

1. Download the latest `AOG-Bogballe-Bridge-vX.X.X-win-x64.zip` from the [Releases](https://github.com/charlesquick/AOG-Bogballe-Bridge/releases) page and extract it anywhere. There is nothing to install and .NET is bundled, so Python is no longer needed.
2. Run `AOG-Bogballe-Bridge.exe`. Windows Firewall may ask you to allow it the first time it runs - allow it, as the Bridge listens for UDP on port `8888`.
3. Select your COM port from the drop-down. Press **Refresh** if you plug the adapter in after starting the Bridge.
4. Load your vehicle in AgOpenGPS. The width and section settings are sent automatically over the network to the Bridge and saved.

Note that the Bridge will only accept machine configurations with 2, 4 or 8 sections. You will see a warning popup if your machine is not supported.

The window shows:

- **UDP** and **Serial** status lights - green when data is arriving from AgIO and the COM port is open
- Live speed, rate and width, and the last PGN received
- The machine configuration received from AgOpenGPS
- The on/off state of each section

The COM port and machine configuration are saved automatically in your Windows user profile, so there is no config file to edit.

If communication with AgIO is lost for more than 1.5 seconds, the Bridge turns off spreading (speed 0, all sections off) until data returns.

### AgOpenGPS Setup

Your tool in AgOpenGPS should be set to use 8 sections, however 2 or 4 section configurations are also supported. Sections must all be the same size.

AgOpenGPS 5.7 or newer is required. You will see a warning if an older version is detected.

Enable UDP in AgIO, and if you don't already have an ethernet-based autosteer system, see the section below.

The tool distance back from the axle should be `distance from headstock to axle + 80cm`. Set turn on/off delays to `0`

The ZURF or TOTZ box will then calculate its own turn-on delay based on forward speed, number of sections active, etc.


### Network Setup

AgIO broadcasts to the subnet set in its Ethernet settings (`192.168.5` by default). The guidance PC must have a network adapter with an address on that subnet, otherwise the broadcasts never reach the Bridge and the UDP light stays red.

If your guidance PC does not already have a network connection on that subnet, then follow these steps to enable UDP comms:

- Create a virtual loopback adapter as per [this guide](https://web.archive.org/web/20221114092633/https://consumer.huawei.com/en/support/content/en-us00693656/), and give it a static address on the AgIO subnet (e.g. `192.168.5.10`, mask `255.255.255.0`).

If you have a USB cell modem, the new virtual interface will take priority, despite it being non-routable. To fix this:

- Open `regedit` and navigate to `HKEY_LOCAL_MACHINE\Software\Microsoft\Wcmsvc`

- Create a new Dword called `IgnoreNonRoutableEthernet` and set its value to `1`

- You will also need to go to `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local`

- Create a new Dword `fMinimizeConnections`, set to `0`


### TOTZ/ZURF/UNIQ Setup

- Make sure your Calibrator has been updated to the latest version. See [here](https://www.bogballe.com/fertiliser-spreaders/software/).

- Go to `menu -> speed input`

- Set it to  `Serial / RS232 input`

That's it!

## Troubleshooting

- **UDP light red** - check AgIO is running and its subnet matches one of the PC's network adapters (see Network Setup).
- **Serial light red** - check the adapter is plugged in, press **Refresh** and re-select the COM port. Make sure no other program has the port open.
- **Sections not shown / "Unsupported" config** - set the tool in AgOpenGPS to 2, 4 or 8 equal sections and reload the vehicle.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows. Open `AOG-Bogballe-Bridge.slnx` in Visual Studio, or build from the command line:

```
dotnet build AOG-Bogballe-Bridge.slnx -c Release
```

To produce the self-contained single-file exe used for releases:

```
dotnet publish AOG-Bogballe-Bridge/AOG-Bogballe-Bridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
```

## Legacy Python version

The original Python console script (`main.py`, `config.ini`, the `Development` folder and `AgOpenGPS Bogballe Bridge.exe`) is still in the repository for reference, but is no longer maintained. Please use the v2.0.0+ release instead.

## TODO
The Bridge was re-written as a native Windows app in 2026 (v2.0.0).
There is always more to do, please feedback with any issues or requests.

- Validation of CRC from AOG PGNs
- Validation of acknowledgements from TOTZ
- Add variable rate control - wait for PGN structure to mature
