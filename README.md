# AOG Bogballe Bridge
## Section control for Bogballe spreaders

This script listens to the autoSteerData and machineData PGNs emitted by AgOpenGPS and sends them over RS232 using the [Bogballe serial protocol](https://dam.bogballe.com/dmm3bwsv3/AssetStream.aspx?mediaformatid=10061&destinationid=10016&assetid=3488)

This should work with all 3 Bogballe spreader controllers: TOTZ, ZURF and UNIQ. If you have any compatibility issues, please let me know.

## Instructions for use

You will need a USB -> RS232 adapter and null modem cable to connect to the port on TOTZ/ZURF/UNIQ.

Ensure your computer has python installed.

Run the `AgOpenGPS Bogballe Bridge.exe` file.

If all goes well, it should install the dependencies, and you will be presented with a warning that your COM port does not exist yet.

Click OK and you will see a list of available COM ports - type in the number of the port you would like to use.
Typing `r` will update the list.

```
Available COM ports:
     COM2
     COM16
     COM24
     COM25
Please select COM number (0, 2, 15..), or r to refresh: 25
```
You will then need to load your vehicle in AgOpenGPS. The width and section settings will be sent automatically over the network to the Bridge and saved.
Note that the Bridge will only accept machine configurations with 2, 4 or 8 sections. You will see a warning popup if your machine is not supported.

The script is now listening to all PGNs broadcast to port `8888` and is extracting speed and section data. This is converted into the Bogballe protocol and sent over the RS232 link.

The COM port and machine data are saved in `config.ini`. To reset the program to defaults, open it in Notepad and set all the values to `0`.

The setting `CommsLostBehaviour`, when set to `0`, will turn off spreading if communication with AgIO is lost (this is the default).
To have the spreader keep following its last instruction after losing communication, set this to `1`.
This is useful in bad signal areas, where network changes can sometimes cause AgIO to lock up.

### AgOpenGPS Setup

Your tool in AgOpenGPS should be set to use 8 sections, however 2 or 4 section configurations are also supported. Sections must all be the same size.

Enable UDP in AgIO, and if you don't already have an ethernet-based autosteer system, see the section below.

The tool distance back from the axle should be `distance from headstock to axle + 80cm`. Set turn on/off delays to `0`

The ZURF or TOTZ box will then calculate its own turn-on delay based on forward speed, number of sections active, etc.


### Network Setup

If your guidance PC does not already have a network connection, then follow these steps to enable UDP comms:

- Create a virtual loopback adapter as per [this guide](https://web.archive.org/web/20221114092633/https://consumer.huawei.com/en/support/content/en-us00693656/).

If you have a USB cell modem, the new virtual interface will take priority, despite it being non-routable. To fix this,

- Open `regedit` and navigate to `HKEY_LOCAL_MACHINE\Software\Microsoft\Wcmsvc`

- Create a new Dword called `IgnoreNonRoutableEthernet` and set its value to `1`

- You will also need to go to `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local`

- Create a new Dword `fMinimizeConnections`, set to `0`




### TOTZ/ZURF/UNIQ Setup

- Go to `menu -> speed input`

- Set it to  `Serial / RS232 input`

That's it!


## TODO
This code was mostly re-written in January 2023 to be more reliable and universal.
There is always more to do, please feedback with any issues or requests.

- Validation of CRC from AOG PGNs
- Validation of acknowledgements from TOTZ
- Add variable rate control - wait for PGN structure to mature
