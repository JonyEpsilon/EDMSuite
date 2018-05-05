﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

using NationalInstruments;
using NationalInstruments.DAQmx;
using DAQ.Environment;
using DAQ.HAL;

namespace ConfocalControl
{
    class DFGPlugin
    {
        #region Class members

        // Dependencies should refer to this instance only 
        private static DFGPlugin controllerInstance;
        public static DFGPlugin GetController()
        {
            if (controllerInstance == null)
            {
                controllerInstance = new DFGPlugin();
            }
            return controllerInstance;
        }

        // Define Opt state
        private enum DFGState { stopped, running, stopping };
        private DFGState backendState = DFGState.stopped;
        private enum WavemeterScanState { stopped, running, stopping };
        private WavemeterScanState wavemeterState = WavemeterScanState.stopped;
        private enum TripletScanState { stopped, running, stopping };
        private TripletScanState tripletState = TripletScanState.stopped;

        // Settings
        public PluginSettings Settings { get; set; }

        // Laser
        private ICEBlocDFG dfg;
        public ICEBlocDFG DFG { get { return dfg; } }

        // Bound event managers to class
        // Wavemeter scan
        public event SpectraScanDataEventHandler WavemeterData;
        public event MultiChannelScanFinishedEventHandler WavemeterScanFinished;
        public event SpectraScanExceptionEventHandler WavemeterScanProblem;
        // Triplet search
        public event TeraSingleDataEventHandler TripletData;

        // Constants relating to sample acquisition
        private int MINNUMBEROFSAMPLES = 10;
        private double TRUESAMPLERATE = 1000;
        private int pointsPerExposure;
        private double sampleRate;

        // Keep track of data for wavemeter scan
        private List<Point>[] wavemeterAnalogBuffer;
        private List<Point>[] wavemeterCounterBuffer;
        public Hashtable wavemeterHistoricSettings;

        // Keep track of data for triplet search
        private double[,] tripletAnalogBuffer;
        private List<double[]> tripletCounterBuffer;
        public Hashtable tripletHistoricSettings;

        // Keep track of tasks
        private Task triggerTask;
        private DigitalSingleChannelWriter triggerWriter;
        private Task freqOutTask;
        private List<Task> counterTasks;
        private List<CounterSingleChannelReader> counterReaders;
        private Task analoguesTask;
        private AnalogMultiChannelReader analoguesReader;

        #endregion

        #region Initialization

        public void LoadSettings()
        {
            Settings = PluginSaveLoad.LoadSettings("DFG");
        }

        public DFGPlugin()
        {
            dfg = new ICEBlocDFG();

            LoadSettings();
            if (Settings.Keys.Count != 16)
            {
                Settings["wavelength"] = 1200.0;

                Settings["wavemeterScanStart"] = 1200.0;
                Settings["wavemeterScanStop"] = 1300.0;
                Settings["wavemeterScanPoints"] = 101;

                Settings["counterChannels"] = new List<string> { "APD0" };
                Settings["analogueChannels"] = new List<string> { };
                Settings["analogueLowHighs"] = new Dictionary<string, double[]>();

                Settings["wavemeter_channel_type"] = "Counters";
                Settings["wavemeter_display_channel_index"] = 0;

                Settings["tripletStart"] = 1200.0;
                Settings["tripletStop"] = 1300.0;
                Settings["tripletScanPoints"] = 101;
                Settings["tripletInt"] = 2000.0;
                Settings["tripletRate"] = 10000.0;

                Settings["triplet_channel_type"] = "Counters";
                Settings["triplet_display_channel_index"] = 0;
            }

            wavemeterHistoricSettings = new Hashtable();
            wavemeterHistoricSettings["counterChannels"] = (List<string>)Settings["counterChannels"];
            wavemeterHistoricSettings["analogueChannels"] = (List<string>)Settings["analogueChannels"];

            tripletHistoricSettings = new Hashtable();
            tripletHistoricSettings["counterChannels"] = (List<string>)Settings["counterChannels"];
            tripletHistoricSettings["analogueChannels"] = (List<string>)Settings["analogueChannels"];

            triggerTask = null;
            freqOutTask = null;
            counterTasks = null;
            analoguesTask = null;

            triggerWriter = null;
            counterReaders = null;
            analoguesReader = null;
        }

        #endregion

        #region Shared functions

        public bool IsRunning()
        {
            return backendState == DFGState.running;
        }

        public bool WavemeterScanIsRunning()
        {
            return wavemeterState == WavemeterScanState.running;
        }

        public bool TripletScanIsRunning()
        {
            return tripletState == TripletScanState.running;
        }

        private bool CheckIfStopping()
        {
            return backendState == DFGState.stopping;
        }

        public void StopAcquisition()
        {
            backendState = DFGState.stopping;
        }

        private int SetAndLockWavelength(double wavelength)
        {
            return dfg.wavelength("mir", wavelength, true);
        }

        #endregion

        #region Wavemeter Scan

        private void CalculateParameters()
        {
            double _sampleRate = (double)TimeTracePlugin.GetController().Settings["sampleRate"];
            if (_sampleRate * MINNUMBEROFSAMPLES >= TRUESAMPLERATE)
            {
                pointsPerExposure = MINNUMBEROFSAMPLES;
                sampleRate = _sampleRate * pointsPerExposure;
            }
            else
            {
                pointsPerExposure = Convert.ToInt32(TRUESAMPLERATE / _sampleRate);
                sampleRate = _sampleRate * pointsPerExposure;
            }
        }

        public bool WavemeterAcceptableSettings()
        {
            if ((double)Settings["wavemeterScanStart"] >= (double)Settings["wavemeterScanStop"] || (int)Settings["wavemeterScanPoints"] < 1)
            {
                MessageBox.Show("Wavemeter scan settings unacceptable.");
                return false;
            }
            else
            {
                return true;
            }
        }

        public void WavemeterSynchronousStartScan()
        {
            try
            {
                if (IsRunning() || TimeTracePlugin.GetController().IsRunning() || FastMultiChannelRasterScan.GetController().IsRunning() || CounterOptimizationPlugin.GetController().IsRunning() || SolsTiSPlugin.GetController().IsRunning())
                {
                    throw new DaqException("Daq already running");
                }

                wavemeterState = WavemeterScanState.running;
                backendState = DFGState.running;
                wavemeterHistoricSettings["wavemeterScanStart"] = (double)Settings["wavemeterScanStart"];
                wavemeterHistoricSettings["wavemeterScanStop"] = (double)Settings["wavemeterScanStop"];
                wavemeterHistoricSettings["wavemeterScanPoints"] = (int)Settings["wavemeterScanPoints"];
                wavemeterHistoricSettings["exposure"] = TimeTracePlugin.GetController().GetExposure();
                wavemeterHistoricSettings["counterChannels"] = (List<string>)Settings["counterChannels"];
                wavemeterHistoricSettings["analogueChannels"] = (List<string>)Settings["analogueChannels"];
                wavemeterHistoricSettings["analogueLowHighs"] = (Dictionary<string, double[]>)Settings["analogueLowHighs"];
                WavemeterSynchronousAcquisitionStarting();
                WavemeterSynchronousAcquire();
            }
            catch (Exception e)
            {
                if (WavemeterScanProblem != null) WavemeterScanProblem(e);
            }
        }

        private void WavemeterSynchronousAcquisitionStarting()
        {
            wavemeterAnalogBuffer = new List<Point>[((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count];
            for (int i = 0; i < ((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count; i++)
            {
                wavemeterAnalogBuffer[i] = new List<Point>();
            }
            wavemeterCounterBuffer = new List<Point>[((List<string>)wavemeterHistoricSettings["counterChannels"]).Count];
            for (int i = 0; i < ((List<string>)wavemeterHistoricSettings["counterChannels"]).Count; i++)
            {
                wavemeterCounterBuffer[i] = new List<Point>();
            }

            // Define sample rate 
            CalculateParameters();

            // Set up trigger task
            triggerTask = new Task("pause trigger task");

            // Digital output
            ((DigitalOutputChannel)Environs.Hardware.DigitalOutputChannels["StartTrigger"]).AddToTask(triggerTask);

            triggerTask.Control(TaskAction.Verify);

            DaqStream triggerStream = triggerTask.Stream;
            triggerWriter = new DigitalSingleChannelWriter(triggerStream);

            triggerWriter.WriteSingleSampleSingleLine(true, false);

            // Set up clock task
            freqOutTask = new Task("sample clock task");

            // Finite pulse train
            freqOutTask.COChannels.CreatePulseChannelFrequency(
                ((CounterChannel)Environs.Hardware.CounterChannels["SampleClock"]).PhysicalChannel,
                "photon counter clocking signal",
                COPulseFrequencyUnits.Hertz,
                COPulseIdleState.Low,
                0,
                sampleRate,
                0.5);

            freqOutTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger(
                (string)Environs.Hardware.GetInfo("StartTriggerReader"),
                DigitalEdgeStartTriggerEdge.Rising);

            freqOutTask.Triggers.StartTrigger.Retriggerable = true;


            freqOutTask.Timing.ConfigureImplicit(SampleQuantityMode.FiniteSamples, pointsPerExposure + 1);

            freqOutTask.Control(TaskAction.Verify);

            freqOutTask.Start();

            // Set up edge-counting tasks
            counterTasks = new List<Task>();
            counterReaders = new List<CounterSingleChannelReader>();

            for (int i = 0; i < ((List<string>)wavemeterHistoricSettings["counterChannels"]).Count; i++)
            {
                string channelName = ((List<string>)wavemeterHistoricSettings["counterChannels"])[i];

                counterTasks.Add(new Task("buffered edge counters " + channelName));

                // Count upwards on rising edges starting from zero
                counterTasks[i].CIChannels.CreateCountEdgesChannel(
                    ((CounterChannel)Environs.Hardware.CounterChannels[channelName]).PhysicalChannel,
                    "edge counter " + channelName,
                    CICountEdgesActiveEdge.Rising,
                    0,
                    CICountEdgesCountDirection.Up);

                // Take one sample within a window determined by sample rate using clock task
                counterTasks[i].Timing.ConfigureSampleClock(
                    (string)Environs.Hardware.GetInfo("SampleClockReader"),
                    sampleRate,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.ContinuousSamples);

                counterTasks[i].Control(TaskAction.Verify);

                DaqStream counterStream = counterTasks[i].Stream;
                counterReaders.Add(new CounterSingleChannelReader(counterStream));

                // Start tasks
                counterTasks[i].Start();
            }

            // Set up analogue sampling tasks
            analoguesTask = new Task("analogue sampler");

            for (int i = 0; i < ((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count; i++)
            {
                string channelName = ((List<string>)wavemeterHistoricSettings["analogueChannels"])[i];

                double inputRangeLow = ((Dictionary<string, double[]>)wavemeterHistoricSettings["analogueLowHighs"])[channelName][0];
                double inputRangeHigh = ((Dictionary<string, double[]>)wavemeterHistoricSettings["analogueLowHighs"])[channelName][1];

                ((AnalogInputChannel)Environs.Hardware.AnalogInputChannels[channelName]).AddToTask(
                    analoguesTask,
                    inputRangeLow,
                    inputRangeHigh
                    );
            }

            if (((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count != 0)
            {
                analoguesTask.Timing.ConfigureSampleClock(
                    (string)Environs.Hardware.GetInfo("SampleClockReader"),
                    sampleRate,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.ContinuousSamples);

                analoguesTask.Control(TaskAction.Verify);

                DaqStream analogStream = analoguesTask.Stream;
                analoguesReader = new AnalogMultiChannelReader(analogStream);

                // Start tasks
                analoguesTask.Start();
            }
        }

        private void WavemeterSynchronousAcquire()
        // Main method for looping over scan parameters, aquiring scan outputs and connecting to controller for display
        {
            // Go to start of scan
            int report = SetAndLockWavelength((double)wavemeterHistoricSettings["wavemeterScanStart"]);
            if (report == 1) throw new Exception("set_wave_m start: task failed");

            // Main loop
            for (double i = 0;
                    i < (int)wavemeterHistoricSettings["wavemeterScanPoints"];
                    i++)
            {
                double currentWavelength = (double)wavemeterHistoricSettings["wavemeterScanStart"] + i *
                    ((double)wavemeterHistoricSettings["wavemeterScanStop"] - (double)wavemeterHistoricSettings["wavemeterScanStart"]) /
                    ((int)wavemeterHistoricSettings["wavemeterScanPoints"] - 1);

                report = SetAndLockWavelength(currentWavelength);

                if (report == 0)
                {
                    // Start trigger task
                    triggerWriter.WriteSingleSampleSingleLine(true, true);

                    // Read counter data
                    List<double[]> counterLatestData = new List<double[]>();
                    foreach (CounterSingleChannelReader counterReader in counterReaders)
                    {
                        counterLatestData.Add(counterReader.ReadMultiSampleDouble(pointsPerExposure + 1));
                    }

                    // Read analogue data
                    double[,] analogLatestData = null;
                    if (((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count != 0)
                    {
                        analogLatestData = analoguesReader.ReadMultiSample(pointsPerExposure + 1);
                    }

                    // re-init the trigger 
                    triggerWriter.WriteSingleSampleSingleLine(true, false);

                    // Store counter data
                    for (int j = 0; j < counterLatestData.Count; j++)
                    {
                        double[] latestData = counterLatestData[j];
                        double data = latestData[latestData.Length - 1] - latestData[0];
                        Point pnt = new Point(currentWavelength, data);
                        wavemeterCounterBuffer[j].Add(pnt);
                    }

                    // Store analogue data
                    if (((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count != 0)
                    {
                        for (int j = 0; j < analogLatestData.GetLength(0); j++)
                        {
                            double sum = 0;
                            for (int k = 0; k < analogLatestData.GetLength(1) - 1; k++)
                            {
                                sum = sum + analogLatestData[j, k];
                            }
                            double average = sum / (analogLatestData.GetLength(1) - 1);
                            Point pnt = new Point(currentWavelength, average);
                            wavemeterAnalogBuffer[j].Add(pnt);
                        }
                    }

                    WavemeterOnData();

                    // Check if scan exit.
                    if (CheckIfStopping())
                    {
                        wavemeterState = WavemeterScanState.stopping;
                        report = SetAndLockWavelength((double)wavemeterHistoricSettings["wavemeterScanStart"]);
                        if (report == 1) throw new Exception("set_wave_m end: task failed");
                        // Quit plugins
                        WavemeterAcquisitionFinishing();
                        WavemeterOnScanFinished();
                        return;
                    }
                }
            }

            WavemeterAcquisitionFinishing();
            WavemeterOnScanFinished();
        }

        public void WavemeterAcquisitionFinishing()
        {
            triggerTask.Dispose();
            freqOutTask.Dispose();
            foreach (Task counterTask in counterTasks)
            {
                counterTask.Dispose();
            }
            analoguesTask.Dispose();

            triggerTask = null;
            freqOutTask = null;
            counterTasks = null;
            analoguesTask = null;

            triggerWriter = null;
            counterReaders = null;
            analoguesReader = null;

            wavemeterState = WavemeterScanState.stopped;
            backendState = DFGState.stopped;
        }

        private void WavemeterOnData()
        {
            switch ((string)Settings["wavemeter_channel_type"])
            {
                case "Counters":
                    if (WavemeterData != null)
                    {
                        if ((int)Settings["wavemeter_display_channel_index"] >= 0 && (int)Settings["wavemeter_display_channel_index"] < ((List<string>)wavemeterHistoricSettings["counterChannels"]).Count)
                        {
                            WavemeterData(wavemeterCounterBuffer[(int)Settings["wavemeter_display_channel_index"]].ToArray());
                        }
                        else WavemeterData(null);
                    }
                    break;

                case "Analogues":
                    if (WavemeterData != null)
                    {
                        if ((int)Settings["wavemeter_display_channel_index"] >= 0 && (int)Settings["wavemeter_display_channel_index"] < ((List<string>)wavemeterHistoricSettings["analogueChannels"]).Count)
                        {
                            WavemeterData(wavemeterAnalogBuffer[(int)Settings["wavemeter_display_channel_index"]].ToArray());
                        }
                        else WavemeterData(null);
                    }
                    break;

                default:
                    throw new Exception("Did not understand data type");
            }
        }

        private void WavemeterOnScanFinished()
        {
            if (WavemeterScanFinished != null) WavemeterScanFinished();
        }

        public void RequestWavemeterHistoricData()
        {
            if (wavemeterAnalogBuffer != null && wavemeterCounterBuffer != null) WavemeterOnData();
        }

        public void SaveWavemeterData(string fileName, bool automatic)
        {
            string directory = Environs.FileSystem.GetDataDirectory((string)Environs.FileSystem.Paths["scanMasterDataPath"]);

            List<string> lines = new List<string>();
            lines.Add(DateTime.Today.ToString("dd-MM-yyyy") + " " + DateTime.Now.ToString("HH:mm:ss"));
            lines.Add("Exposure = " + ((double)wavemeterHistoricSettings["exposure"]).ToString());
            lines.Add("Lambda start = " + ((double)wavemeterHistoricSettings["wavemeterScanStart"]).ToString() + ", Lambda stop = " + ((double)wavemeterHistoricSettings["wavemeterScanStop"]).ToString() + ", Lambda resolution = " + ((int)wavemeterHistoricSettings["wavemeterScanPoints"]).ToString());

            string descriptionString = "Lambda ";
            foreach (string channel in (List<string>)wavemeterHistoricSettings["counterChannels"])
            {
                descriptionString = descriptionString + channel + " ";
            }
            foreach (string channel in (List<string>)wavemeterHistoricSettings["analogueChannels"])
            {
                descriptionString = descriptionString + channel + " ";
            }
            lines.Add(descriptionString);

            for (int i = 0; i < wavemeterCounterBuffer[0].Count; i++)
            {
                string line = wavemeterCounterBuffer[0][i].X.ToString() + " ";
                foreach (List<Point> counterData in wavemeterCounterBuffer)
                {
                    line = line + counterData[i].Y.ToString() + " ";
                }
                foreach (List<Point> analogData in wavemeterAnalogBuffer)
                {
                    line = line + analogData[i].Y.ToString() + " ";
                }
                lines.Add(line);
            }

            if (automatic) System.IO.File.WriteAllLines(directory + fileName, lines.ToArray());
            else
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.InitialDirectory = directory;
                saveFileDialog.FileName = fileName;

                if (saveFileDialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllLines(saveFileDialog.FileName, lines.ToArray());
                }
            }
        }

        #endregion

        #region Triplet Scan

        public bool TripletAcceptableSettings()
        {
            if ((double)Settings["tripletStart"] >= (double)Settings["tripletStop"] || (int)Settings["tripletScanPoints"] < 1 || (double)Settings["tripletInt"] <= 0 || (double)Settings["tripletRate"] <= 0)
            {
                MessageBox.Show("Triplet scan settings unacceptable.");
                return false;
            }
            else
            {
                return true;
            }
        }

        public void TripletStartScan()
        {
            try
            {
                if (IsRunning() || TimeTracePlugin.GetController().IsRunning() || FastMultiChannelRasterScan.GetController().IsRunning() || CounterOptimizationPlugin.GetController().IsRunning() || SolsTiSPlugin.GetController().IsRunning())
                {
                    throw new DaqException("Daq already running");
                }

                tripletState = TripletScanState.running;
                backendState = DFGState.running;
                tripletHistoricSettings["tripletStart"] = (double)Settings["tripletStart"];
                tripletHistoricSettings["tripletStop"] = (double)Settings["tripletStop"];
                tripletHistoricSettings["tripletScanPoints"] = (int)Settings["tripletScanPoints"];
                tripletHistoricSettings["tripletInt"] = (double)Settings["tripletInt"];
                tripletHistoricSettings["tripletRate"] = (double)Settings["tripletRate"];
                tripletHistoricSettings["counterChannels"] = (List<string>)Settings["counterChannels"];
                tripletHistoricSettings["analogueChannels"] = (List<string>)Settings["analogueChannels"];
                tripletHistoricSettings["analogueLowHighs"] = (Dictionary<string, double[]>)Settings["analogueLowHighs"];
                TripletAcquisitionStarting();
                TripletAcquire();
            }
            catch (Exception e)
            {
                if (TripletScanProblem != null) TripletScanProblem(e);
            }
        }

        private void TripletAcquisitionStarting()
        {
            pointsPerExposure = Convert.ToInt32((double)tripletHistoricSettings["tripletRate"] * (double)tripletHistoricSettings["tripletInt"]);
            sampleRate = (double)tripletHistoricSettings["tripletRate"];

            // Set up trigger task
            triggerTask = new Task("pause trigger task");

            // Digital output
            ((DigitalOutputChannel)Environs.Hardware.DigitalOutputChannels["StartTrigger"]).AddToTask(triggerTask);

            triggerTask.Control(TaskAction.Verify);

            DaqStream triggerStream = triggerTask.Stream;
            triggerWriter = new DigitalSingleChannelWriter(triggerStream);

            triggerWriter.WriteSingleSampleSingleLine(true, false);

            // Set up clock task
            freqOutTask = new Task("sample clock task");

            // Finite pulse train
            freqOutTask.COChannels.CreatePulseChannelFrequency(
                ((CounterChannel)Environs.Hardware.CounterChannels["SampleClock"]).PhysicalChannel,
                "photon counter clocking signal",
                COPulseFrequencyUnits.Hertz,
                COPulseIdleState.Low,
                0,
                sampleRate,
                0.5);

            freqOutTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger(
                (string)Environs.Hardware.GetInfo("StartTriggerReader"),
                DigitalEdgeStartTriggerEdge.Rising);

            freqOutTask.Triggers.StartTrigger.Retriggerable = true;


            freqOutTask.Timing.ConfigureImplicit(SampleQuantityMode.FiniteSamples, pointsPerExposure + 1);

            freqOutTask.Control(TaskAction.Verify);

            freqOutTask.Start();

            // Set up edge-counting tasks
            counterTasks = new List<Task>();
            counterReaders = new List<CounterSingleChannelReader>();

            for (int i = 0; i < ((List<string>)tripletHistoricSettings["counterChannels"]).Count; i++)
            {
                string channelName = ((List<string>)tripletHistoricSettings["counterChannels"])[i];

                counterTasks.Add(new Task("buffered edge counters " + channelName));

                // Count upwards on rising edges starting from zero
                counterTasks[i].CIChannels.CreateCountEdgesChannel(
                    ((CounterChannel)Environs.Hardware.CounterChannels[channelName]).PhysicalChannel,
                    "edge counter " + channelName,
                    CICountEdgesActiveEdge.Rising,
                    0,
                    CICountEdgesCountDirection.Up);

                // Take one sample within a window determined by sample rate using clock task
                counterTasks[i].Timing.ConfigureSampleClock(
                    (string)Environs.Hardware.GetInfo("SampleClockReader"),
                    sampleRate,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.ContinuousSamples);

                counterTasks[i].Control(TaskAction.Verify);

                DaqStream counterStream = counterTasks[i].Stream;
                counterReaders.Add(new CounterSingleChannelReader(counterStream));

                // Start tasks
                counterTasks[i].Start();
            }

            // Set up analogue sampling tasks
            analoguesTask = new Task("analogue sampler");

            for (int i = 0; i < ((List<string>)tripletHistoricSettings["analogueChannels"]).Count; i++)
            {
                string channelName = ((List<string>)tripletHistoricSettings["analogueChannels"])[i];

                double inputRangeLow = ((Dictionary<string, double[]>)tripletHistoricSettings["analogueLowHighs"])[channelName][0];
                double inputRangeHigh = ((Dictionary<string, double[]>)tripletHistoricSettings["analogueLowHighs"])[channelName][1];

                ((AnalogInputChannel)Environs.Hardware.AnalogInputChannels[channelName]).AddToTask(
                    analoguesTask,
                    inputRangeLow,
                    inputRangeHigh
                    );
            }

            if (((List<string>)tripletHistoricSettings["analogueChannels"]).Count != 0)
            {
                analoguesTask.Timing.ConfigureSampleClock(
                    (string)Environs.Hardware.GetInfo("SampleClockReader"),
                    sampleRate,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.ContinuousSamples);

                analoguesTask.Control(TaskAction.Verify);

                DaqStream analogStream = analoguesTask.Stream;
                analoguesReader = new AnalogMultiChannelReader(analogStream);

                // Start tasks
                analoguesTask.Start();
            }
        }

        private void TripletAcquire()
        // Main method for looping over scan parameters, aquiring scan outputs and connecting to controller for display
        {
            // Go to start of scan
            int report = SetAndLockWavelength((double)tripletHistoricSettings["tripletStart"]);
            if (report == 1) throw new Exception("set_wave_m start: task failed");

            // Main loop
            for (double i = 0;
                    i < (int)tripletHistoricSettings["tripletScanPoints"];
                    i++)
            {
                double currentWavelength = (double)tripletHistoricSettings["tripletStart"] + i *
                    ((double)tripletHistoricSettings["tripletStop"] - (double)tripletHistoricSettings["tripletStart"]) /
                    ((int)tripletHistoricSettings["tripletScanPoints"] - 1);

                report = SetAndLockWavelength(currentWavelength);

                if (report == 0)
                {
                    // Start trigger task
                    triggerWriter.WriteSingleSampleSingleLine(true, true);

                    // Read counter data
                    tripletCounterBuffer = new List<double[]>();
                    foreach (CounterSingleChannelReader counterReader in counterReaders)
                    {
                        tripletCounterBuffer.Add(counterReader.ReadMultiSampleDouble(pointsPerExposure + 1));
                    }

                    // Read analogue data
                    if (((List<string>)tripletHistoricSettings["analogueChannels"]).Count != 0)
                    {
                        tripletAnalogBuffer = analoguesReader.ReadMultiSample(pointsPerExposure + 1);
                    }

                    // re-init the trigger 
                    triggerWriter.WriteSingleSampleSingleLine(true, false);

                    // Broadcast
                    TripletOnData();

                    // Save data
                    TripletSaveData();

                    // Check if scan exit.
                    if (CheckIfStopping())
                    {
                        CHANGE
                        wavemeterState = WavemeterScanState.stopping;
                        report = SetAndLockWavelength((double)Settings["wavemeterScanStart"]);
                        if (report == 1) throw new Exception("set_wave_m end: task failed");
                        // Quit plugins
                        WavemeterAcquisitionFinishing();
                        WavemeterOnScanFinished();
                        return;
                    }
                }
            }

            CHANGE
            WavemeterAcquisitionFinishing();
            WavemeterOnScanFinished();
        }

        private void TripletOnData()
        {
            switch ((string)Settings["triplet_channel_type"])
            {
                case "Counters":
                    if (TripletData != null)
                    {
                        if ((int)Settings["triplet_display_channel_index"] >= 0 && (int)Settings["triplet_display_channel_index"] < ((List<string>)tripletHistoricSettings["counterChannels"]).Count)
                        {
                            TripletData(tripletCounterBuffer[(int)Settings["triplet_display_channel_index"]].ToArray());
                        }
                        else TripletData(null);
                    }
                    break;

                case "Analogues":
                    if (TripletData != null)
                    {
                        if ((int)Settings["triplet_display_channel_index"] >= 0 && (int)Settings["triplet_display_channel_index"] < ((List<string>)tripletHistoricSettings["analogueChannels"]).Count)
                        {
                            int row_size = tripletAnalogBuffer.GetLength(1);
                            TripletData(tripletAnalogBuffer.Cast<double>().Skip((int)Settings["triplet_display_channel_index"] * row_size).Take(row_size).ToArray();
                        }
                        else TripletData(null);
                    }
                    break;

                default:
                    throw new Exception("Did not understand data type");
            }
        }

        private void TripletSaveData(double currentWavelength)
        {
            string directory = Environs.FileSystem.GetDataDirectory((string)Environs.FileSystem.Paths["scanMasterDataPath"]);

            NEWDIRECTORY

            List<string> lines = new List<string>();
            lines.Add(DateTime.Today.ToString("dd-MM-yyyy") + " " + DateTime.Now.ToString("HH:mm:ss"));
            lines.Add("Rate = " + ((double)tripletHistoricSettings["tripletRate"]).ToString() + ", Int = " + ((double)tripletHistoricSettings["tripletInt"]).ToString());
            lines.Add("Lambda start = " + ((double)tripletHistoricSettings["tripletStart"]).ToString() + ", Lambda stop = " + ((double)tripletHistoricSettings["tripletStop"]).ToString() + ", Lambda resolution = " + ((int)tripletHistoricSettings["tripletScanPoints"]).ToString());
            lines.Add("Current lambda = " + currentWavelength.ToString());

            string descriptionString = "Lambda ";
            foreach (string channel in (List<string>)tripletHistoricSettings["counterChannels"])
            {
                descriptionString = descriptionString + channel + " ";
            }
            foreach (string channel in (List<string>)tripletHistoricSettings["analogueChannels"])
            {
                descriptionString = descriptionString + channel + " ";
            }
            lines.Add(descriptionString);

            for (int i = 0; i < tripletCounterBuffer[0].Length; i++)
            {
                string line = tripletCounterBuffer[0][i].ToString() + " ";
                foreach (double[] counterData in tripletCounterBuffer)
                {
                    line = line + counterData[i].ToString() + " ";
                }
                int column_size = tripletAnalogBuffer.GetLength(0);
                for (int j = 0; j < column_size; j++)
			    {
			        line = line + tripletAnalogBuffer[j,i].ToString() + " ";
			    }
                lines.Add(line);
            }

            System.IO.File.WriteAllLines(directory + fileName, lines.ToArray());
        }

        #endregion
    }
}