module.exports = {
    socket: null,
    dataWriter: null,
    dataReader: null,

    connect: function (success, fail, args) {
        try {
            var deviceName = args[0].toLowerCase();

            // execute async
            setTimeout(function () {
                var rfcomm = Windows.Devices.Bluetooth.Rfcomm;
                var sockets = Windows.Networking.Sockets;
                var streams = Windows.Storage.Streams;
                var query = rfcomm.RfcommDeviceService.getDeviceSelector(rfcomm.RfcommServiceId.serialPort);
                var me = this;

                Windows.Devices.Enumeration.DeviceInformation.findAllAsync(query, null)
                    .then(function (pairedDevices) {
                        for (var idx = 0; idx < pairedDevices.length; idx++) {
                            if (pairedDevices[idx].name.toLowerCase() == deviceName) {
                                return rfcomm.RfcommDeviceService.fromIdAsync(pairedDevices[idx].id);
                            };
                        }
                        throw new Error('Device not found');
                    }).then(function (device) {
                        me.socket = new sockets.StreamSocket();
                        return me.socket.connectAsync(device.connectionHostName, device.connectionServiceName, sockets.SocketProtectionLevel.plainSocket);
                    }).then(function () { // success
                        me.dataWriter = new streams.DataWriter(me.socket.outputStream);
                        me.dataReader = new streams.DataReader(me.socket.inputStream);
                        me.dataReader.byteOrder = streams.ByteOrder.littleEndian;
                    }).done(success, fail);
            }, 0);
        } catch (ex) {
            fail(ex);
        }
    },
    write: function (success, fail, args) {
        try {

            if (this.dataWriter == null) {
                fail('Not connected');
                return;
            }

            var data = args[0];

            this.dataWriter.writeBytes(data);
            this.dataWriter.storeAsync().done(success, fail);

        } catch (ex) {
            fail(ex);
        }
    },
    read: function (success, fail, args) {

        if (args.length != 1) {
            faile('invalid arguments, please specify number of bytes to read');
            return;
        }

        if (this.dataReader == null) {
            fail('Not connected');
            return;
        }

        var numBytes = args[0],
            me = this;

        setTimeout(function () {
            me.dataReader.loadAsync(numBytes).then(function () {
                var array = new Array(numBytes);
                me.dataReader.readBytes(array)
                return array;
            }).done(success, fail);
        }, 0);
    }
};
require("cordova/windows8/commandProxy").add("BluetoothSerial", module.exports);