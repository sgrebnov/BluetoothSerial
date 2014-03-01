/*  
    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at
    
    http://www.apache.org/licenses/LICENSE-2.0
    
    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.
*/

/*
 Known issues:
    •   Sometimes startAdvertising and discoverDevices functions return an error because native PeerFinder.start method throws an exception (SDK bug?)
    •   If any plugin function was called after read function the app stops handling incoming messages. 
    •   Connect function uses PeerInformation.DisplayName property to connect to found peer instead of using peer address because native PeerInformation.HostName property sometimes returns null value.
*/

using System.Threading;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Tasks;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Windows.Networking.Proximity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using WPCordovaClassLib.Cordova;
using WPCordovaClassLib.Cordova.Commands;
using WPCordovaClassLib.Cordova.JSON;

namespace Cordova.Extension.Commands
{
    /// <summary>
    /// Provides access to Bluetooth functionality.
    /// </summary>
    class BluetoothSerial : BaseCommand
    {
        #region fields of the class

        /// <summary>
        /// Found peers.
        /// </summary>
        private static IReadOnlyList<PeerInformation> discoveredPeers;

        /// <summary>
        /// Bluetooth states.
        /// </summary>
        private enum BluetoothState
        {
            Disabled = 0,
            Enabled = 1            
        }

        /// <summary>
        /// Connection types.
        /// </summary>
        public enum ConnectionType
        {
            PhoneToPhone = 0,
            PhoneToDevice = 1
        }

        /// <summary>
        /// Bluetooth requested state.
        /// </summary>
        private BluetoothState requestedState;

        /// <summary>
        /// Connection socket to transfer message via Bluetooth.
        /// </summary>
        private StreamSocket connectionSocket;

        /// <summary>
        /// Data reader.
        /// </summary>
        private DataReader dataReader;

        /// <summary>
        /// Data writer.
        /// </summary>
        private DataWriter dataWriter;

        /// <summary>
        /// Indicates whether Bluetooth settings dialog was open or not.
        /// </summary>
        private bool isBluetoothSettingsOpen;

        /// <summary>
        /// Indicates whether the app is listening to incoming message or not
        /// </summary>
        private bool isListeningEnabled;

        #endregion

        #region peer information

        /// <summary>
        /// Represents Peer information.
        /// </summary>
        [DataContract]
        public class PeerInfo
        {
            /// <summary>
            /// Peer name.
            /// </summary>
            [DataMember(Name = "name", IsRequired = false)]
            public string Name { get; set; }

            /// <summary>
            /// Peer address.
            /// </summary>
            [DataMember(Name = "address", IsRequired = false)]
            public string Address { get; set; }

            /// <summary>
            /// Constructor of the class.
            /// </summary>
            /// <param name="name">Peer name.</param>
            /// <param name="address">Peer address.</param>
            public PeerInfo(string name, string address)
            {
                this.Name = name;
                this.Address = address;
            }
        }

        #endregion

        #region connection options

        /// <summary>
        /// Represents Bluetooth connection options.
        /// </summary>
        [DataContract]
        public class ConnectionOptions
        {
            /// <summary>
            /// Peer address.
            /// </summary>
            [DataMember(Name = "address", IsRequired = false)]
            public string Address { get; set; }

            /// <summary>
            /// Connection type.
            /// </summary>
            [DataMember(Name = "type", IsRequired = false)]
            public ConnectionType Type { get; set; }

            /// <summary>
            /// Message to be sent.
            /// </summary>
            [DataMember(Name = "message", IsRequired = false)]
            public string Message { get; set; }

            /// <summary>
            /// Indicates whether the app should continue advertising after the connection was closed.
            /// </summary>
            [DataMember(Name = "continueAdvertise", IsRequired = false)]
            public bool ContinueAdvertise { get; set; }            
        }

        #endregion

        #region public methods

        /// <summary>
        /// Checks current Bluetooth state. 
        /// </summary>
        /// <param name="options"></param>
        public void isEnabled(string options)
        {                        
            this.DetectBluetoothState(state =>
                {
                    bool isEnabled = Convert.ToBoolean(state);
                    this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, JsonHelper.Serialize(isEnabled)));
                });
        }

        /// <summary>
        /// Enables Bluetooth adapter
        /// </summary>
        /// <param name="options"></param>
        public void enable(string options)
        {
            this.requestedState = BluetoothState.Enabled;
            this.ChangeBluetoothState();
        }

        /// <summary>
        /// Disables Bluetooth adapter
        /// </summary>
        /// <param name="options"></param>
        public void disable(string options)
        {
            this.requestedState = BluetoothState.Disabled;
            this.ChangeBluetoothState();            
        }     
   
        /// <summary>
        /// Searches for Bluetooth peers
        /// </summary>
        /// <param name="options"></param>
        public async void discoverDevices(string options)
        {
            try
            {                
                discoveredPeers = await PeerFinder.FindAllPeersAsync();
                PeerInfo[] peerInfo = new PeerInfo[discoveredPeers.Count];

                for (int i = 0; i < discoveredPeers.Count; i++)
                {
                    var peer = discoveredPeers[i];
                    
                    //TODO It seems PeerInformation.HostName property sometimes returns null. Check what is the cause.
                    string hostName = peer.HostName == null ? "Unknown host" : peer.HostName.DisplayName;
                    peerInfo[i] = new PeerInfo(peer.DisplayName, hostName);                   
                }

                this.DispatchCommandResult(discoveredPeers.Count > 0
                                               ? new PluginResult(PluginResult.Status.OK, JsonHelper.Serialize(peerInfo))
                                               : new PluginResult(PluginResult.Status.ERROR, "No devices were found"));
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Error occurred while discovering devices"));
            }            
        }

        /// <summary>
        /// Connects to a phone or a device
        /// </summary>
        /// <param name="options"></param>
        public async void connect(string options)
        {
            var connectionOptions = new ConnectionOptions {Type = ConnectionType.PhoneToDevice};

            try
            {
                string[] args = JsonHelper.Deserialize<string[]>(options);
                connectionOptions.Address = args[0];
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            if (string.IsNullOrEmpty(connectionOptions.Address))
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            try
            {

                if (discoveredPeers == null)
                {
                    PeerFinder.AlternateIdentities["Bluetooth:PAIRED"] = "";
                    discoveredPeers = await PeerFinder.FindAllPeersAsync();
                }

                PeerInformation peer = null;
                foreach (var discoveredDevice in discoveredPeers)
                {
                    //TODO It seems PeerInformation.HostName property sometimes returns null. So we connect to a phone/device by name instead of host address
                    if (string.Compare(discoveredDevice.DisplayName, connectionOptions.Address, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        peer = discoveredDevice;
                        break;
                    }
                }

                if (peer == null)
                {
                    this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Unable to find the following peer: " + connectionOptions.Address));
                    return;
                }

                if (connectionOptions.Type == ConnectionType.PhoneToPhone)
                {
                    connectionSocket = await PeerFinder.ConnectAsync(peer);        
                }
                else
                {
                    connectionSocket = new StreamSocket();
                    await connectionSocket.ConnectAsync(peer.HostName, "1");
                }

                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK));
                
                
            }
            catch (Exception ex)
            {
                var message = "Error occurred while connecting to the peer"; // default error message
                if ((uint)ex.HResult == 0x8007048F)
                {
                    message = "Bluetooth is turned off";
                }
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, message));
            }
        }

        /// <summary>
        /// Closes current connection
        /// </summary>
        /// <param name="options"></param>
        public void disconnect(string options)
        {
            ConnectionOptions connectionOptions;

            try
            {
                string[] args = JsonHelper.Deserialize<string[]>(options);
                connectionOptions = JsonHelper.Deserialize<ConnectionOptions>(args[0]);
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            try
            {
                this.CloseConnection(connectionOptions != null && connectionOptions.ContinueAdvertise);
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK));
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Error occurred while disconnecting"));
            }
        }
        
        /// <summary>
        /// Starts the process of finding a peer app and makes an app discoverable to remote peers.
        /// </summary>
        /// <param name="options"></param>
        public void startAdvertising(string options)
        {
            try
            {                
                PeerFinder.ConnectionRequested += OnConnectionRequested;
                PeerFinder.Start();
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK));
            }
            catch (Exception e)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Error occurred while starting advertising"));
            }            
        }

        /// <summary>
        /// Reads incoming message.
        /// </summary>
        /// <param name="options"></param>
        public async void read(string options)
        {
            uint numBytes;
            string callbackId;

            try
            {
                string[] args = JsonHelper.Deserialize<string[]>(options);
                numBytes = JsonHelper.Deserialize<uint>(args[0]);
                callbackId = args[1];
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            try
            {
                var bytes = await ReadBytesAsync(numBytes);

                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, bytes), callbackId);
            }
            catch (Exception ex)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, ex.Message));
            }
        }

        #endregion

        #region connection methods

        /// <summary>
        /// Connects to incoming request.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void OnConnectionRequested(object sender, ConnectionRequestedEventArgs args)
        {
            try
            {
                connectionSocket = await PeerFinder.ConnectAsync(args.PeerInformation);
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Error occurred while processing incoming request"));
            }            
        }

        /// <summary>
        /// Closes existing Bluetooth connection.
        /// </summary>
        /// <param name="continueAdvertise">indicates whether the app should be discoverable to remote peers after closing connection. </param>
        private void CloseConnection(bool continueAdvertise)
        {
            PeerFinder.Stop();
            this.isListeningEnabled = false;

            if (dataReader != null)
            {
                dataReader.Dispose();
                dataReader = null;
            }

            if (dataWriter != null)
            {
                dataWriter.Dispose();
                dataWriter = null;
            }

            if (connectionSocket != null)
            {
                connectionSocket.Dispose();
                connectionSocket = null;
            }
            
            if (continueAdvertise)
            {
                PeerFinder.Start();
            }
        }

        #endregion

        #region state related methods
                
        /// <summary>
        /// OnResume event handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public override void OnResume(object sender, ActivatedEventArgs e)
        {
            if (!this.isBluetoothSettingsOpen)
            {
                return;
            }
            PhoneApplicationService service = PhoneApplicationService.Current;
            service.Activated -= this.OnResume;

            this.isBluetoothSettingsOpen = false;

            this.DetectBluetoothState(state =>
            {
                PluginResult.Status status = (state == this.requestedState) ? PluginResult.Status.OK : PluginResult.Status.ERROR;
                this.DispatchCommandResult(new PluginResult(status));
            });
        }

        /// <summary>
        /// Opens Bluetooth settings dialog.
        /// </summary>
        private void OpenBluetoothSettings()
        {
            try
            {
                PhoneApplicationService service = PhoneApplicationService.Current;
                service.Activated += this.OnResume;

                ConnectionSettingsTask connectionSettingsTask = new ConnectionSettingsTask();
                connectionSettingsTask.ConnectionSettingsType = ConnectionSettingsType.Bluetooth;
                connectionSettingsTask.Show();
                this.isBluetoothSettingsOpen = true;
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Unable to open Bluetooth settings"));
            }
        }

        /// <summary>
        /// Checks whether current Bluetooth state matches requested state and opens connection settings in negative case.
        /// </summary>
        private void ChangeBluetoothState()
        {
            this.DetectBluetoothState(state =>
            {
                if (state == this.requestedState)
                {
                    this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK));
                    return;
                }
                
                this.OpenBluetoothSettings();
            });
        }
        
        /// <summary>
        /// Detects current Bluetooth requestedState
        /// </summary>
        private async void DetectBluetoothState(Action<BluetoothState> action)
        {
            BluetoothState state = BluetoothState.Enabled;

            try
            {
                PeerFinder.AlternateIdentities["Bluetooth:Paired"] = "";
                var peers = await PeerFinder.FindAllPeersAsync();     
                
            }
            catch (Exception ex)
            {                
                if ((uint)ex.HResult == 0x8007048F)
                {
                    state = BluetoothState.Disabled;
                }
            }

            action(state);
        }

        #endregion

        /// <summary>
        /// Writes binary data via Bluetooth.
        /// </summary>
        /// <param name="message">Data to be sent.</param>
        public async void write(string options)
        {
            List<byte> data;

            string callbackId;

            try
            {
                string[] args = JsonHelper.Deserialize<string[]>(options);
                data = JsonHelper.Deserialize<List<byte>>(args[0]);
                callbackId = args[1];
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }
            
            
            if (data == null || data.Count == 0)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Data is null"));
                return;
            }

            if (connectionSocket == null)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Socket is null. Check connection."));
                return;
            }

            if (dataWriter == null)
            {
                dataWriter = new DataWriter(connectionSocket.OutputStream);
            }

            try
            {

                dataWriter.WriteBytes(data.ToArray());
                dataWriter.StoreAsync();

                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK), callbackId);
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, "Error occurred while sending message"));
            }
        }

        static ManualResetEvent readHandler = new ManualResetEvent(true);

        private async Task<byte[]> ReadBytesAsync(uint numBytes)
        {
            if (dataReader == null)
            {
                dataReader = new DataReader(connectionSocket.InputStream);
            }
            byte[] buffer = new byte[numBytes];

            readHandler.WaitOne();
            try
            {
                await dataReader.LoadAsync(numBytes);
                dataReader.ReadBytes(buffer);
            }
            finally
            {
                readHandler.Set();
            }
            return buffer;
        }

    }
}