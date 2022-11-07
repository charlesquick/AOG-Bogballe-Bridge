# AOG Bogballe Bridge
## Section control for Bogballe spreaders

This script listens to the autoSteerData and machineData PGNs emitted by AgOpenGPS and sends them over RS232 using the [Bogballe serial protocol](https://dam.bogballe.com/dmm3bwsv3/AssetStream.aspx?mediaformatid=10061&destinationid=10016&assetid=3488)

The code is currently an initial proof-of-concept hacked together in less than 24 hours. It's probably not very reliable. The functionality is slightly different between the TOTZ and ZURF boxes, so please select the appropriate file.

## Instructions for use

You will need a USB -> RS232 adapter and null modem cable to connect to the port on TOTZ/ZURF.

Ensure your computer has python installed with the pySerial module. You may need to run `python -m pip install pyserial` in the command prompt

Run totz.py or zurf.py. You will be presented with a list of available COM ports - type in the index of the port you would like to use.

```
Available COM ports
0 :  COM2
1 :  COM16
2 :  COM24
3 :  COM25
Please select COM port (0, 1, 2,..):1
```

in the zurf version, also input your working width.

`Please enter working width in meters: 24`

That's it!

The script is now listening to all PGNs broadcast to port 8888 and is extracting speed and section data. This is converted into the Bogballe protocol and sent over the RS232 link.

### AgOpenGPS Setup

Your implement in AgOpenGPS must be set to use 8 sections divided equally across the width of the machine.

Enable UDP in AgIO, and if you don't already have an ethernet-based autosteer system, set the ip address to `127.0.0.1`

#### NEW in Version 5.6.x

You will not be able to set the IP address to `127.0.0.1` in AOG. Instead you will need to create a virtual loopback adapter as per [this guide] (https://consumer.huawei.com/en/support/content/en-us00693656/).

If you have a USB cell modem, the new virtual interface will take priority, despite it being non-routable. To fix this, open `regedit` and navigate to `HKEY_LOCAL_MACHINE\Software\Microsoft\Wcmsvc`
Create a new Dword called `IgnoreNonRoutableEthernet` and set its value to `1`

You may also need to go to `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local` and create a new Dword `fMinimizeConnections` set to `0`




The distance back from the axle should be `distance from headstock to axle + 80cm`. Set turn on/off delays to `0`

The Bogballe box will then calculate its own turn-on delay based on forward speed, number of sections active, etc.

### TOTZ/ZURF Setup

You will need to make sure the total working width matches that set in AgOpenGPS.

Under `menu/speed input`, set it to  `Serial / RS232 input`




## TODO
I anticipate almost completely re-writing this code over the next few weeks to streamline the data flow, improve reliability and usability.

Particularly:

- Config file to store COM port information
- Avoid the clumsy and slow data manipulations currently in use
- Validation of CRC from AOG PGNs
- Validation of acknowledgements from TOTZ
- Implement variable rate control
- Small GUI / web interface to make port selection easy, show debugging data
