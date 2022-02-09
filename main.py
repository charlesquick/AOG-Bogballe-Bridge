import socket
import serial
import serial.tools.list_ports

localIP = ""
localPort = 8888
bufferSize = 1024
UDPServerSocket = socket.socket(family=socket.AF_INET, type=socket.SOCK_DGRAM)
UDPServerSocket.bind((localIP, localPort))
print("UDP server up and listening")

sections = [0, 0, 0, 0, 0, 0, 0, 0]
speed = 0.0
print("Available COM ports")
a = 0
for comport in serial.tools.list_ports.comports():
    print(a, ": ", comport.device)
    a += 1
input1 = int(input("Please select COM port (0, 1, 2,..): "))
ser = serial.Serial(serial.tools.list_ports.comports()[input1].device, 9600, timeout=0.04)


def extractData(input):
    if(input[3:4].hex()) == 'ef':
        sectBits = f'{int((input[11:12].hex()), 16):0>8b}'
        a = 7
        for i in sectBits:
            sections[a] = i
            a -= 1
        return
    if (input[3:4].hex()) == 'fe':
        spd = int.from_bytes(bytearray.fromhex(input[5:6].hex() + ' ' + input[6:7].hex()), byteorder='little', signed=False)
        global speed
        speed = round((spd * 0.1), 1)
        return

def checksum(input):
    hexList = []
    for x in input:
        hexList.append(hex(ord(x)))
    out = hexList[0]
    for i in range(1, len(hexList)):
        out = hex(int(out, 16) ^ int(hexList[i], 16))

    if out == ("0x00" or "0x7b" or "0x7d"):
        out = "0x55"

    return out[2:]


def sendSpeed():
    sendBuf = "S:SpdKmh:" + str(speed) + ":"
    cs = checksum(sendBuf)
    sendBuf2 = b'\x7b'
    sendBuf2 += bytes(sendBuf, 'ASCII')
    sendBuf2 += bytearray.fromhex(cs)
    sendBuf2 += b'\x7d'

    #sendBuf = b'\x7B\x53\x42\x32\x34\x30\x27\x7D' #known working test sequence
    #print("s: " + str(sendBuf2))
    ser.write(sendBuf2)
    recvBuf = ser.read(20)
    #print("r: " + str(recvBuf))
    return

def sendSections():
    sendBuf = "S:MOrlBs:1:-1.0:"
    for i in sections:
        sendBuf += str(i) + ":"
    cs = checksum(sendBuf)
    sendBuf2 = b'\x7b'
    sendBuf2 += bytes(sendBuf, 'ASCII')
    sendBuf2 += bytearray.fromhex(cs)
    sendBuf2 += b'\x7d'
    #print("s: " + str(sendBuf2))
    ser.write(sendBuf2)
    recvBuf = ser.read(33)
    #print("r: " + str(recvBuf))
    return


#if __name__ == "__main__":
    #try:
while True:
    bytesAddressPair = UDPServerSocket.recvfrom(bufferSize)
    message = bytesAddressPair[0]
    address = bytesAddressPair[1]
    extractData(message)
    sendSpeed()
    sendSections()

    #except KeyboardInterrupt:
    #    ser.close()
     #   exit()


