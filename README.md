# AOG Bogballe Bridge
## Section control for Bogballe spreaders

This script listens to the autoSteerData and machineData PGNs emitted by AgOpenGPS and sends them over RS232 using the [Bogballe serial protocol](https://dam.bogballe.com/dmm3bwsv3/AssetStream.aspx?mediaformatid=10061&destinationid=10016&assetid=3488)

The code is currently an initial proof-of-concept hacked together in less than 24 hours. It's probably not very reliable. The functionality has been tested with a Calibrator TOTZ, however it should work with ZURF boxes too.

## Instructions for use

You will need a USB -> RS232 adapter and null modem cable to connect to the port on TOTZ/ZURF.

Run main.py. You will be presented with a list of available COM ports - type in the index of the port you would like to use.

```
Available COM ports
0 :  COM2
1 :  COM16
2 :  COM24
3 :  COM25
Please select COM port (0, 1, 2,..):1
```
That's it!

The script is now listening to all PGNs broadcast to port 8888 and is extracting speed and section data. This is converted into the Bogballe protocol and sent over the RS232 link.

### AgOpenGPS Setup

Your implement in AgOpenGPS must be set to use 8 sections divided equally across the width of the machine.

### TOTZ/ZURF Setup

You will need to make sure the total working width matches that set in AgOpenGPS.

Under `menu/speed input`, set it to  `Serial / RS232 input`
