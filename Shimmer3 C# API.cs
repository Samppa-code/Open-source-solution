using ShimmerAPI;
using ShimmerLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using SharpLSL;


namespace ShimmerConsoleAppExample
{
    class Program
    {
        Filter LPF_PPG;
        Filter HPF_PPG;
        PPGToHRAlgorithm PPGtoHeartRateCalculation;
        int NumberOfHeartBeatsToAverage = 1;
        int TrainingPeriodPPG = 10; //10 second buffer
        double LPF_CORNER_FREQ_HZ = 5;
        double HPF_CORNER_FREQ_HZ = 0.5;
        ShimmerLogAndStreamSystemSerialPort Shimmer;
        double SamplingRate = 128;
        int Count = 0;
        bool FirstTime = true;
        StreamOutlet outlet;

        //The index of the signals originating from ShimmerBluetooth 
        int IndexAccelX;
        int IndexAccelY;
        int IndexAccelZ;
        int IndexGSR;
        int IndexPPG;
        int IndexTimeStamp;

        static async Task Main(string[] args)
        {
            System.Console.WriteLine("Hello");
            Program p = new Program();
            await p.start();
        }

        public async Task start()
        {

            // Initialize LSL stream for GSR
            StreamInfo info = new StreamInfo(
                "ShimmerGSR",
                "GSR",
                1,
                SamplingRate,
                SharpLSL.ChannelFormat.Double,
                "shimmer3gsr"
                );

            outlet = new StreamOutlet(info);

            //Setup PPG to HR filters and algorithm
            PPGtoHeartRateCalculation = new PPGToHRAlgorithm(SamplingRate, NumberOfHeartBeatsToAverage, TrainingPeriodPPG);
            LPF_PPG = new Filter(Filter.LOW_PASS, SamplingRate, new double[] { LPF_CORNER_FREQ_HZ });
            HPF_PPG = new Filter(Filter.HIGH_PASS, SamplingRate, new double[] { HPF_CORNER_FREQ_HZ });

            int enabledSensors = ((int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GSR | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13);
      

            // Try all available COM ports and pick the first that connects
            string[] ports = SerialPort.GetPortNames();
            if (ports == null || ports.Length == 0)
            {
                Console.WriteLine("No COM ports found.");
                return;
            }

            const int connectTimeoutMs = 3000;
            bool found = false;

            foreach (var port in ports.OrderBy(p => p))
            {
                Console.WriteLine("Trying port " + port + "...");
                var candidate = new ShimmerLogAndStreamSystemSerialPort("ShimmerID1", port, SamplingRate, 0, ShimmerBluetooth.GSR_RANGE_AUTO, enabledSensors, false, false, false, 1, 0, Shimmer3Configuration.EXG_EMG_CONFIGURATION_CHIP1, Shimmer3Configuration.EXG_EMG_CONFIGURATION_CHIP2, true);

                var tcs = new TaskCompletionSource<bool>();
                EventHandler handler = null;
                handler = (sender, args) =>
                {
                    try
                    {
                        CustomEventArgs ev = (CustomEventArgs)args;
                        if (ev.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE)
                        {
                            int state = (int)ev.getObject();
                            if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                            {
                                tcs.TrySetResult(true);
                            }
                            else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                            {
                                // treat as failure (device didn't attach)
                                tcs.TrySetResult(false);
                            }
                        }
                    }
                    catch
                    {
                        tcs.TrySetResult(false);
                    }
                };

                candidate.UICallback += handler;

                try
                {
                    // Connect (may be synchronous or async inside)
                    candidate.Connect();

                    // Wait for connected state or timeout
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(connectTimeoutMs));
                    if (completed == tcs.Task && tcs.Task.Result)
                    {
                        // success
                        Console.WriteLine("Connected on " + port);
                        // adopt this candidate as the main Shimmer instance
                        candidate.UICallback -= handler; // remove temporary handler
                        Shimmer = candidate;
                        Shimmer.UICallback += this.HandleEvent;
                        found = true;              
                        await this.delayedWork();

                        break;
                    }
                    else
                    {
                        // not connected within timeout or failed
                        candidate.UICallback -= handler;
                        try { candidate.Disconnect(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error trying port {port}: {ex.Message}");
                    candidate.UICallback -= handler;
                    try { candidate.Disconnect(); } catch { }
                }
            }

            if (!found)
            {
                Console.WriteLine("No Shimmer device found on available COM ports.");
                return;
            }

            // if we reach here, Shimmer is set and connected; nothing else to do
        }
        public void HandleEvent(object sender, EventArgs args)
        {
            CustomEventArgs eventArgs = (CustomEventArgs)args;
            int indicator = eventArgs.getIndicator();

            switch (indicator)
            {
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE:
                    System.Diagnostics.Debug.Write(((ShimmerBluetooth)sender).GetDeviceName() + " State = " + ((ShimmerBluetooth)sender).GetStateString() + System.Environment.NewLine);
                    int state = (int)eventArgs.getObject();
                    if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                    {
                        System.Console.WriteLine("Shimmer is Connected");
                        Task ignoredAwaitableResult = this.delayedWork();
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
                    {
                        System.Console.WriteLine("Establishing Connection to Shimmer Device");
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                    {
                        System.Console.WriteLine("Shimmer is Disconnected");
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                    {
                        System.Console.WriteLine("Shimmer is Streaming");
                    }
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE:
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET:
                    ObjectCluster objectCluster = (ObjectCluster)eventArgs.getObject();
                    if (FirstTime)
                    {
                        IndexAccelX = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL);
                        IndexAccelY = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL);
                        IndexAccelZ = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL);
                        IndexGSR = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.GSR, ShimmerConfiguration.SignalFormats.CAL);
                        if (Shimmer.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                        {
                            IndexPPG = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13, ShimmerConfiguration.SignalFormats.CAL);
                        }
                        else
                        {
                            IndexPPG = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14, ShimmerConfiguration.SignalFormats.CAL);
                        }
                        IndexTimeStamp = objectCluster.GetIndex(ShimmerConfiguration.SignalNames.SYSTEM_TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL);
                        FirstTime = false;
                    }
                    SensorData datax = objectCluster.GetData(IndexAccelX);
                    SensorData datay = objectCluster.GetData(IndexAccelY);
                    SensorData dataz = objectCluster.GetData(IndexAccelZ);
                    SensorData dataGSR = objectCluster.GetData(IndexGSR);
                    SensorData dataPPG = objectCluster.GetData(IndexPPG);
                    SensorData dataTS = objectCluster.GetData(IndexTimeStamp);

                    // --- LSL SEND GSR SAMPLE ---                    
                    if (outlet != null)
                    {
                        double[] sample = new double[] { dataGSR.Data };
                        outlet.PushSample(sample);
                    }

                    //Process PPG signal and calculate heart rate
                    double dataFilteredLP = LPF_PPG.filterData(dataPPG.Data);
                    double dataFilteredHP = HPF_PPG.filterData(dataFilteredLP);
                    int heartRate = (int)Math.Round(PPGtoHeartRateCalculation.ppgToHrConversion(dataFilteredHP, dataTS.Data));


                    if (Count % SamplingRate == 0) //only display data every second
                    {
                        System.Console.WriteLine("AccelX: " + datax.Data + " " + datax.Unit + " AccelY: " + datay.Data + " " + datay.Unit + " AccelZ: " + dataz.Data + " " + dataz.Unit);
                        System.Console.WriteLine("Time Stamp: " + dataTS.Data + " " + dataTS.Unit + " GSR: " + dataGSR.Data + " " + dataGSR.Unit + " PPG: " + dataPPG.Data + " " + dataPPG.Unit + " HR: " + heartRate + " BPM");
                    }
                    Count++;
                    break;
            }
        }

        private async Task delayedWork()
        {
            await Task.Delay(1000);
            Shimmer.StartStreaming();
        }

    }
}
