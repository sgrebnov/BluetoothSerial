module.exports = {
    channel: null,
    service: null,
    dataWriter: null,

    connect: function (success, fail, args) {
        try {
            var deviceName = args[0]; 

            // execute async
            setTimeout(function() {
                var rfcomm = Windows.Devices.Bluetooth.Rfcomm;
                var sockets = Windows.Networking.Sockets;
                var streams = Windows.Storage.Streams;
                var query = rfcomm.RfcommDeviceService.getDeviceSelector(rfcomm.RfcommServiceId.serialPort);

                var me = this;
                // TODO - re-write
                Windows.Devices.Enumeration.DeviceInformation.findAllAsync(query)
                   .then(function (pairedDevices) {
                       return new WinJS.Promise(function (complete, error, progress) {
                           for (var idx = 0; idx < pairedDevices.length; idx++) {
                               if (pairedDevices[idx].name.toLowerCase() == deviceName) {
                                   rfcomm.RfcommDeviceService.fromIdAsync(pairedDevices[idx].id).then(function (device) {
                                       if (device != null) {
                                           complete(device);
                                       }
                                   });
                               }
                           };
                       });
                   }).then(function(device) {
                       me.service = device;
                       me.channel = new sockets.StreamSocket();
                       return me.channel.connectAsync(me.service.connectionHostName, me.service.connectionServiceName, sockets.SocketProtectionLevel.plainSocket);
               }).done(
                   function() { // success
                       me.dataWriter = new streams.DataWriter(me.channel.outputStream);
                       success();
                   }, fail);
            },0);

        } catch(ex) {
            fail(ex);
        }

    },
    write: function (success, fail, args) {
        try {
            
            if(this.dataWriter == null) {
                fail('Not connected');
                return;
            }

            var data = args[0];

            this.dataWriter.writeBytes(data);
            this.dataWriter.storeAsync().done(success, fail);

        } catch(ex) {
            fail(ex);
        }
    }
};
require("cordova/windows8/commandProxy").add("BluetoothSerial", module.exports);
