namespace ControlNetworkServerExampleModule
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    using Oxy.DigitalTwinLibrary;
    using Oxy.ControlNetwork;
    using Oxy.ControlNetwork.TCP;

    class ControlNetworkServerExampleModule
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        int counter;
        Twin twin = null;

        IControlNetworkNode controlNetworkNode = null;
        IControlNetworkTopic<List<RollPitchYawDatum>> rollPitchYawTopic = null;

        private ControlNetworkServerExampleModule() {

        }

        private void Start() {
            #if DEBUG
            Console.WriteLine("Waiting for debugger to attach");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("Debugger attached");
            #endif

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();

            Init(cts).Wait();

            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
            this.controlNetworkNode.StopNode();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        async Task Init(CancellationTokenSource cts)
        {
            try{
                Console.WriteLine("ControlNetworkServerExampleModule initializing.");
                MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
                ITransportSettings[] settings = { mqttSetting };

                // Open a connection to the Edge runtime
                ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
                await ioTHubModuleClient.OpenAsync();
                Console.WriteLine("ControlNetworkServerExampleModule initialized.");

                twin = await ioTHubModuleClient.GetTwinAsync();

                // try to read the ControlNetwork config properrties from the Module Twin
                ControlNetworkConfig config = new ControlNetworkConfig();
                if(twin.Properties.Desired.Contains("ControlNetwork")) {
                    var controlNetworkTwin = twin.Properties.Desired["ControlNetwork"];
                    if(controlNetworkTwin.ContainsKey("config")) {
                        var controlNetworkConfigTwin = controlNetworkTwin["config"];

                        try{
                            config = (ControlNetworkConfig) JsonConvert
                                .DeserializeObject<ControlNetworkConfig>(controlNetworkConfigTwin.ToString());
                        } catch (JsonSerializationException) {
                            Console.Error.WriteLine($"Error Deserializeing ControlNetwork config from: {controlNetworkConfigTwin}");
                            cts.Cancel();
                        }
                    }
                }
                
                // Now initialize the ControlNetwork
                this.controlNetworkNode = ControlNetworkTCPMessagingNodeFactory.CreateNode(config);
                this.rollPitchYawTopic = this.controlNetworkNode.JoinControlNetworkTopic<List<RollPitchYawDatum>>("RollPitchYawTopic");
                this.rollPitchYawTopic.AddMessageListener(TopicMessageHandler);
                this.controlNetworkNode.StartNode();

                // Register callback to be called when a message is received by the module
                await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", handleSensorMessage, ioTHubModuleClient);
                await ioTHubModuleClient.SetInputMessageHandlerAsync("input2", handleSensorMessage, ioTHubModuleClient);
            } catch(Exception e) {
                Console.Error.WriteLine($"Initializtion error: {e}");
                cts.Cancel();
            }
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        async Task<MessageResponse> handleSensorMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                using (var pipeMessage = new Message(messageBytes))
                {
                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }
                    //await moduleClient.SendEventAsync("output1", pipeMessage);
                    await Task.Delay(100);
                
                    Console.WriteLine("Received message sent");
                }
            }
            return MessageResponse.Completed;
        }

        static void TopicMessageHandler(IControlNetworkMessage<List<RollPitchYawDatum>> msg)
        {
            logger.Info("New message: {0}", msg.ToString());
            Console.Out.Flush();
        }
        static void Main(string[] args)
        {
            (new ControlNetworkServerExampleModule()).Start();
        }
    }
}
