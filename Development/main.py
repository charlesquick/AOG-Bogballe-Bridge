import socket
import serial.tools.list_ports
import ctypes
from configparser import ConfigParser

# Declarations
localIP = ""
localPort = 8888
bufferSize = 1024
UDPServerSocket = socket.socket(family=socket.AF_INET, type=socket.SOCK_DGRAM)


config = ConfigParser()
config.read('config.ini')

sections = [0, 0, 0, 0, 0, 0, 0, 0]
speed = 0.0
secnum = config.getint('main', 'secnum')
secwidth = config.getfloat('main', 'secwidth')
commsLostBehaviour = config.getint('main', 'CommsLostBehaviour')    # If true, stop spreader output on socket timeout
activeSections = 0
activeSectionsLast = 0
AOGversion = 0
com = config.get('main', 'com')
portExists = False
validConf = True
AgIOSaysHello = False
socket_timeout = False


def extractdata(input):
    if (input[3:4].hex()) == 'ef':  # Machine Data PGN
        sectBits = f'{int(input[11:12].hex(), 16):0>8b}'  # Byte 11, sections 1-8
        a = 7
        global activeSections
        activeSections = 0
        for i in sectBits:
            sections[a] = i
            a -= 1
            activeSections += int(i)
        return

    if (input[3:4].hex()) == 'fe':  # Steer data PGN for speed data
        spd = int.from_bytes(bytearray.fromhex(input[5:6].hex() + ' ' + input[6:7].hex()),
                             byteorder='little', signed=False)
        global speed
        speed = round((spd * 0.1), 1)
        return

    if (input[3:4].hex()) == 'eb':  # SectionDimensions PGN
        global secnum, secwidth, validConf
        secwidth = int.from_bytes(bytearray.fromhex(input[5:6].hex() + ' ' + input[6:7].hex()),
                                  byteorder='little', signed=False)
        secnum = int(input[37:38].hex())
        print("Section Configuration Received")
        print(secnum, " sections, each", secwidth, " cm")
        print("Total width:", secwidth / 100 * secnum)
        sendwidth()

        if secnum > 8 or ((secnum % 2) != 0):  # check if configuration is valid (8 or less sections, divisible by 2)
            validConf = False
            errmsg = "Machine configuration not supported. \nPlease use either 2, 4 or 8 sections. \n\n" \
                     "Current setup: " + str(secnum) + " sections, each " + str(secwidth) + " cm wide"
            title = 'AOG-Bogballe Bridge'
            ctypes.windll.user32.MessageBoxW(0, errmsg, title, 0x1000 | 0x30)
        else:
            validConf = True

        config.set('main', 'secnum', str(secnum))
        config.set('main', 'secwidth', str(secwidth))
        with open('config.ini', 'w') as f:
            config.write(f)
        return

    if (input[3:4].hex()) == 'c8':  # AgIO Hello PGN
        global AgIOSaysHello, AOGversion
        AOGversion = int(input[5:6].hex(), 16)
        AgIOSaysHello = True
        return


def checksum(input):
    hexList = []
    for x in input:
        hexList.append(hex(ord(x)))     # hex of the int of the character
    out = hexList[0]
    for i in range(1, len(hexList)):
        out = hex(int(out, 16) ^ int(hexList[i], 16))   # XOR the ints together in base16 then convert to hex

    if out == ("0x00" or "0x7b" or "0x7d"):
        out = "0x55"

    return out[2:]


def sendspeed():
    sendbuf = "S:SpdKmh:" + str(speed) + ":"
    cs = checksum(sendbuf)
    sendbuf2 = b'\x7b'
    sendbuf2 += bytes(sendbuf, 'ASCII')
    sendbuf2 += bytearray.fromhex(cs)
    sendbuf2 += b'\x7d'
    # sendbuf = b'\x7B\x53\x42\x32\x34\x30\x27\x7D'     # known working test sequence
    # print("s: " + str(sendbuf2))
    ser.write(sendbuf2)
    # recvBuf = ser.read(20)
    # print("r: " + str(recvBuf))
    return


def sendwidth():
    sendbuf = "S:SprdWt:" + str(round(secwidth * secnum / 100, 1))
    cs = checksum(sendbuf)
    sendbuf2 = b'\x7b'
    sendbuf2 += bytes(sendbuf, 'ASCII')
    sendbuf2 += bytearray.fromhex(cs)
    sendbuf2 += b'\x7d'
    # print(str(sendbuf2))
    # ser.write(sendbuf2)
    # recvBuf = ser.read(20)
    return


def sendenable():
    global activeSectionsLast
    if activeSections != activeSectionsLast:  # only send enable command when sections change
        if activeSections > 0:
            sendbuf = "S:SOrlSE:1:"
        else:
            sendbuf = "S:SOrlSE:0:"

        activeSectionsLast = activeSections
        cs = checksum(sendbuf)
        sendbuf2 = b'\x7b'
        sendbuf2 += bytes(sendbuf, 'ASCII')
        sendbuf2 += bytearray.fromhex(cs)
        sendbuf2 += b'\x7d'
        ser.write(sendbuf2)
        sendactivewidth()
        # print(str(sendbuf2))


def sendsections():
    sendbuf = "S:SOrlBs:"
    for i in sections[0:secnum]:  # loop through sections
        x = 0
        while x < 8 / secnum:  # Convert 2 or 4 section control into 8
            sendbuf += str(i) + ":"
            x += 1

    cs = checksum(sendbuf)
    sendbuf2 = b'\x7b'
    sendbuf2 += bytes(sendbuf, 'ASCII')
    sendbuf2 += bytearray.fromhex(cs)
    sendbuf2 += b'\x7d'
    # print(str(sendbuf2))
    ser.write(sendbuf2)
    # recvbuf = ser.read(33)
    # print("r: " + str(recvBuf))
    return


def sendactivewidth():
    activewidth = round((activeSections * secwidth / 100), 1)
    sendbuf = "S:SOrlWt:1:" + str(activewidth) + ":"
    cs = checksum(sendbuf)
    sendbuf2 = b'\x7b'
    sendbuf2 += bytes(sendbuf, 'ASCII')
    sendbuf2 += bytearray.fromhex(cs)
    sendbuf2 += b'\x7d'
    # print(str(sendbuf2))
    ser.write(sendbuf2)
    # recvbuf = ser.read(33)
    # print("r: " + str(recvBuf))
    return


def listcom():
    print("Available COM ports:")
    global portExists
    for comport in serial.tools.list_ports.comports():  # Enumerate COM ports
        print("    ", comport.device)
        if com == comport.device:  # Does the saved port match?
            portExists = True
    return


def selectport():
    global com
    a = input("Please select COM number (0, 2, 15..), or r to refresh: ")
    if a == 'r':
        listcom()
        selectport()
    else:
        com = 'COM' + a
    return

def getUDPdata():
    try:
        bytesAddressPair = UDPServerSocket.recvfrom(bufferSize)
        message = bytesAddressPair[0]
        address = bytesAddressPair[1]
    except socket.timeout:
        if not commsLostBehaviour:
            global speed, sections, activeSections, AgIOSaysHello, socket_timeout
            speed = 0
            sections = [0, 0, 0, 0, 0, 0, 0, 0]
            activeSections = 0
        AgIOSaysHello = False
        socket_timeout = True
        return
    extractdata(message)

def flush_socket():
    UDPServerSocket.setblocking(False)
    while True:
        try:
            data = UDPServerSocket.recv(1024)
            if not data:
                break
        except BlockingIOError:
            UDPServerSocket.setblocking(True)
            break


# Setup
UDPServerSocket.bind((localIP, localPort))
print("CSEQ Technologies LTD")
print("AOG-Bogballe Bridge \n")
listcom()
if com == -1 or portExists is False:
    errmsg = "COM port does not exist! \nPlease check your wiring or select a valid COM port"
    title = 'AOG-Bogballe Bridge'
    ctypes.windll.user32.MessageBoxW(0, errmsg, title, 0x1000 | 0x30)
    selectport()
    config.set('main', 'com', str(com))
    with open('config.ini', 'w') as f:
        config.write(f)

print('Using ' + str(com))
ser = serial.Serial(com, 9600, timeout=0.04)
print("Serial port connected \n")
if secnum == 0 or secnum > 8 or ((secnum % 2) != 0):
    errmsg = "Invalid section data! \nPlease re-load the vehicle file in AgOpenGPS"
    title = 'AOG-Bogballe Bridge'
    ctypes.windll.user32.MessageBoxW(0, errmsg, title, 0x1000 | 0x30)
    validConf = False

if validConf:
    print("Using saved machine configuration:")
    print("     ", secnum, " sections, each", secwidth, " cm")
    print("     Total width:", secwidth / 100 * secnum)

print("\nChecking connection to AgIO...")
UDPServerSocket.settimeout(10)
while not AgIOSaysHello:
    getUDPdata()
    if socket_timeout:
        print("Not connected to AgIO")
        socket_timeout = False



if AgIOSaysHello:
    print("Connected to AgIO")
    print("AgOpenGPS Version: ", AOGversion / 10)
    if AOGversion < 56:
        print("\nWARNING \nThis version of AgOpenGPS is not supported! Some features may not work as intended. "
              "\nConsider upgrading to version 5.7 or newer.")
flush_socket()
UDPServerSocket.settimeout(3)

try:
    while True:
        getUDPdata()
        sendspeed()
        if validConf:
            sendenable()
            sendsections()
        if socket_timeout:
            print("Not connected to AgIO")
            errmsg = "Lost Communication to AgIO!"
            title = 'AOG-Bogballe Bridge'
            ctypes.windll.user32.MessageBoxW(0, errmsg, title, 0x1000 | 0x30)
            while not AgIOSaysHello:
                getUDPdata()
            if AgIOSaysHello:
                print("Connected to AgIO")
                socket_timeout = False
                flush_socket()


except serial.serialutil.SerialException:
    ser.close()
    errmsg = "COM port unplugged! \nPlease check your wiring and re-open Bridge"
    title = 'AOG-Bogballe Bridge'
    ctypes.windll.user32.MessageBoxW(0, errmsg, title, 0x1000 | 0x30)
    exit()
