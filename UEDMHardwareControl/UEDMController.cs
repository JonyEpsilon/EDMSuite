﻿using System;
using System.Timers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Runtime.Remoting.Lifetime;
using System.Windows.Forms;
using NationalInstruments;
using NationalInstruments.DAQmx;
using NationalInstruments.VisaNS;
using System.Linq;
using System.IO.Ports;
using System.Windows.Forms.DataVisualization.Charting;

using DAQ.HAL;
using DAQ.Environment;
using System.Diagnostics;
using Data;

namespace UEDMHardwareControl
{
    public class UEDMController : MarshalByRefObject
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        # region Setup
        
        // hardware
        private static string[] Names = { "Cell Temperature Monitor", "S1 Temperature Monitor", "S2 Temperature Monitor", "SF6 Temperature Monitor"};
        private static string[] ChannelNames = { "cellTemperatureMonitor", "S1TemperatureMonitor", "S2TemperatureMonitor", "SF6TemperatureMonitor" };

        LakeShore336TemperatureController tempController = (LakeShore336TemperatureController)Environs.Hardware.Instruments["tempController"];
        AgilentFRG720Gauge sourcePressureMonitor = new AgilentFRG720Gauge("Pressure gauge source", "pressureGauge_source");
        AgilentFRG720Gauge beamlinePressureMonitor = new AgilentFRG720Gauge("Pressure gauge beamline", "pressureGauge_beamline");
        SiliconDiodeTemperatureMonitors tempMonitors = new SiliconDiodeTemperatureMonitors(Names, ChannelNames);

        FlowControllerMKSPR4000B neonFlowController = (FlowControllerMKSPR4000B)Environs.Hardware.Instruments["neonFlowController"];

        Hashtable digitalTasks = new Hashtable();
        Task cryoTriggerDigitalOutputTask;
        Task heatersS2TriggerDigitalOutputTask;
        Task heatersS1TriggerDigitalOutputTask;

        // without this method, any remote connections to this object will time out after
        // five minutes of inactivity.
        // It just overrides the lifetime lease system completely.
        public override Object InitializeLifetimeService()
        {
            return null;
        }

        # endregion

        ControlWindow window;

        public void Start()
        {
            // make the digital tasks
            CreateDigitalTask("cryoTriggerDigitalOutputTask");
            CreateDigitalTask("heatersS2TriggerDigitalOutputTask");
            CreateDigitalTask("heatersS1TriggerDigitalOutputTask");

            // digitial input tasks

            // analog outputs
            //bBoxAnalogOutputTask = CreateAnalogOutputTask("bScan");

            // analog inputs
            //probeMonitorInputTask = CreateAnalogInputTask("probePD", 0, 5);
            


            // make the control window
            window = new ControlWindow();
            window.controller = this;

            Application.Run(window);
        }

        // this method runs immediately after the GUI sets up
        internal void WindowLoaded()
        {
            // Set initial datetime picker values for the user interface
            DateTime now = DateTime.Now;
            DateTime InitialDateTime = new DateTime(now.Year, now.Month, now.AddDays(1).Day, 4, 0, 0);
            window.SetDateTimePickerValue(window.dateTimePickerStopHeatingAndTurnCryoOn, InitialDateTime);
            window.SetDateTimePickerValue(window.dateTimePickerHeatersTurnOff, InitialDateTime);
            window.SetDateTimePickerValue(window.dateTimePickerRefreshModeTurnHeatersOff, InitialDateTime);
            // Set flags
            refreshModeHeaterTurnOffDateTimeFlag = false;
            refreshModeCryoTurnOnDateTimeFlag = false;
            // Check that the LakeShore relay is set correctly 
            InitializeCryoControl();
        }

        public void WindowClosing()
        {
            // Request that the PT monitoring thread stop
            StopPTMonitorPoll();
        }

        private void CreateDigitalTask(String name)
        {
            Task digitalTask = new Task(name);
            ((DigitalOutputChannel)Environs.Hardware.DigitalOutputChannels[name]).AddToTask(digitalTask);
            digitalTask.Control(TaskAction.Verify);
            digitalTasks.Add(name, digitalTask);
        }

        private void SetDigitalLine(string name, bool value)
        {
            Task digitalTask = ((Task)digitalTasks[name]);
            DigitalSingleChannelWriter writer = new DigitalSingleChannelWriter(digitalTask.Stream);
            writer.WriteSingleSampleSingleLine(true, value);
            digitalTask.Control(TaskAction.Unreserve);
        }


        #region Menu controls

        public void Exit()
        {
            Application.Exit();
        }
        /// <summary>
        /// Function to save image of the current state of a plot in the application. 
        /// </summary>
        /// <param name="mychart"></param>
        public void SavePlotImage(Chart mychart)
        {
            Stream myStream;
            SaveFileDialog ff = new SaveFileDialog();

            ff.Filter = "jpg files (*.jpg)|*.jpg|All files (*.*)|*.*";
            ff.FilterIndex = 1;
            ff.RestoreDirectory = true;

            if (ff.ShowDialog() == DialogResult.OK)
            {
	            if ((myStream = ff.OpenFile()) != null)
	            {
		            using (myStream)
		            {
                        mychart.SaveImage(myStream, System.Drawing.Imaging.ImageFormat.Jpeg);
		            }
	            }
	         }
        }

        
        public string[] csvData;
        public void SavePlotDataToCSV(string csvContent)// to be created
        {
            // Displays a SaveFileDialog so the user can save the data
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();
            saveFileDialog1.Filter = "CSV|*.csv";
            saveFileDialog1.Title = "Save a CSV File";
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                // If the file name is not an empty string open it for saving.
                if (saveFileDialog1.FileName != "")
                {
                    // Using stream writer class the chart points are exported. Create an instance of the stream writer class.
                    System.IO.StreamWriter file = new System.IO.StreamWriter(saveFileDialog1.FileName);

                    // Write the datapoints into the file.
                    file.WriteLine(csvContent);

                    file.Close();
                }


            }
            
            //foreach (Series series in myChart.Series)
            //{
            //    string seriesName = series.Name;
            //    int pointCount = series.Points.Count;
            //    csvContent += "Unix Time Stamp (s)" + "," + "Full date/time" + "," + seriesName + " (" + Units + ")" + "\r\n"; // Header lines
            //
            //    for (int p = 0; p < pointCount; p++)
            //    {
            //        var points = series.Points[p];
            //
            //        string yValuesCSV = String.Empty;
            //
            //        DateTime xDateTime = DateTime.FromOADate(points.XValue); // points.XValue is assumed to be a double. It must be converted to a datetime so that we can choose the datetime string format easily.
            //        Int32 unixTimestamp = (Int32)(xDateTime.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            //        string csvLine = unixTimestamp + "," + xDateTime.ToString("dd/MM/yyyy hh:mm:ss") + "," + points.YValues[0];
            //
            //        csvContent += csvLine + "\r\n";
            //
            //    }
            //    csvContent += "\r\n";
            //}
        }

        #endregion

        #region Cryo Control

        private int RelayNumber = 1;
        private string Off = "0"; // I.e. connected in normally open state (NO)
        private string On = "1"; // Swap these if the wiring is changed to normally closed (NC)

        public void EnableCryoDigitalControl(bool enable) // Temporary function - used to control a digital output which connects to the cryo (true = on, false = off)
        {
            SetDigitalLine("cryoTriggerDigitalOutputTask", enable);
        }

        public void InitializeCryoControl()
        {
            string status = tempController.QueryRelayStatus(RelayNumber); // Query the status of the relay.
            if (status == On)
            {
                string message = "Cryo control warning: The LakeShore 336 temperature controller Relay-" + RelayNumber + " is set such that the cryo will turn be on. \n\nClick OK to continue opening application.";
                string caption = "Cryo Control Warning";
                MessageBox.Show(message, caption, MessageBoxButtons.OK);
                window.SetTextBox(window.tbCryoState, "ON");
            }
            else
            {
                if(status == Off) window.SetTextBox(window.tbCryoState, "OFF");
                else
                {
                    window.SetTextBox(window.tbCryoState, "UNKNOWN");
                    string message = "Cryo control exception: When querying the state of LakeShore 336 temperature controller Relay-" + RelayNumber + ", an unexpected response was provided. The state of the relay is UNKNOWN. \n\nClick OK to continue.";
                    string caption = "Cryo Control Exception";
                    MessageBox.Show(message, caption, MessageBoxButtons.OK);
                }
            }
        }

        public string PollCryoStatus()
        {
            string status = tempController.QueryRelayStatus(RelayNumber); // Query the status of the relay.
            if (status == Off | status == On)
            {
                if (status == Off) window.SetTextBox(window.tbCryoState, "OFF");
                else window.SetTextBox(window.tbCryoState, "ON");
            }
            else
            {
                window.SetTextBox(window.tbCryoState, "UNKNOWN");
                string message = "Cryo control exception: When querying the state of LakeShore 336 temperature controller Relay-" + RelayNumber + ", an unexpected response was provided. It is likely that the cryo has not been turned on. \n\nClick OK to continue.";
                string caption = "Cryo Control Exception";
                MessageBox.Show(message, caption, MessageBoxButtons.OK);
            }
            return status;
        }

        public void SetCryoState(bool setState)
        {
            if (setState) TurnOnCryoCooler();
            else TurnOffCryoCooler();
        }

        private void TurnOnCryoCooler()
        {
            string status = PollCryoStatus(); // Query the status of the relay.
            if(status == Off) // If off, then try to turn it on
            {
                bool relaySuccessFlag = tempController.SetRelayParameters(RelayNumber, Int32.Parse(On)); //Turn cryo on
                // Now check if the LakeShore relay has been changed correctly
                string newstatus = PollCryoStatus(); 
                if (newstatus == Off)// Cryo is still off - throw an exception
                {
                    string message = "Cryo control exception: The LakeShore 336 temperature controller Relay-" + RelayNumber + " has not been correctly changed. This means that the cryo cooler has not been turned on.\n\nClick OK to continue.";
                    string caption = "Cryo Control Exception";
                    MessageBox.Show(message, caption, MessageBoxButtons.OK);
                }
            }
            else 
            {
                if (status == On) // The cryo was already turned on! Has some element of hardware been changed manually? Throw exception.
                {
                    string message = "Cryo control exception: Relay was already set such that the cryo should be on! Was the relay changed manually? Is the cryo set to remote mode?\n\nClick OK to continue.";
                    string caption = "Cryo Control Exception";
                    MessageBox.Show(message, caption, MessageBoxButtons.OK);
                }
            }
        }

        private void TurnOffCryoCooler()
        {
            string status = PollCryoStatus(); // Query the status of the relay.
            if (status == On) // If on, then try to turn it off
            {
                bool relaySuccessFlag = tempController.SetRelayParameters(RelayNumber, Int32.Parse(Off));
                // Now check if the LakeShore relay has been changed correctly
                string newstatus = PollCryoStatus();
                if (newstatus == On)// Cryo is still on - throw an exception
                {
                    string message = "Cryo control exception: The LakeShore 336 temperature controller Relay-" + RelayNumber + " has not been correctly changed. This means that the cryo cooler has not been turned off.\n\nClick OK to continue.";
                    string caption = "Cryo Control Exception";
                    MessageBox.Show(message, caption, MessageBoxButtons.OK);
                }
            }
            else 
            {
                if (status == Off) // The cryo was already turned off! Has some element of hardware been changed manually? Throw exception.
                {
                    string message = "Cryo control exception: Relay was already set such that the cryo should be off! Was the relay changed manually? Is the cryo set to remote mode?\n\nClick OK to continue.";
                    string caption = "Cryo Control Exception";
                    MessageBox.Show(message, caption, MessageBoxButtons.OK);
                }
            }
        }

        private void SetHeaterSetpoint(int Output, double Value)
        {
            if(Value > 0)
            {
                tempController.SetControlSetpoint(Output, Value);
            }
        }

        private void EnableLakeGoreHeaterOutput3or4(int Output, bool OnOff)
        {
            if (OnOff)
            {
                tempController.SetHeaterRange(Output, 1); // true = on
                HeatersEnabled = true;
            }
            else
            {
                tempController.SetHeaterRange(Output, 0); // false = off
                HeatersEnabled = false;
            }
        }

        /// <summary>
        /// Sets the range parameter for a given output.
        /// LakeShore 336 outputs 1 and 2 are the PID loop controlled heaters (100 Watts and 50 Watts respectively).
        /// For outputs 1 and 2: 0 = Off, 1 = Low, 2 = Medium and 3= High.
        /// </summary>
        /// <param name="Output"></param>
        /// <param name="range"></param>
        private void EnableLakeGoreHeaterOutput1or2(int Output, int range)
        {
            if (range != 0)
            {
                tempController.SetHeaterRange(Output, range); // 1 = Low, 2 = Medium and 3= High
                HeatersEnabled = true;
            }
            else
            {
                tempController.SetHeaterRange(Output, range); // 0 = Off
                HeatersEnabled = false;
            }
        }

        private void IsOutputEnabled(int Output)
        {
            string HeaterOutput = tempController.QueryHeaterRange(Output);
            string trimResponse = HeaterOutput.Trim();// Trim in case there are unexpected white spaces.
            string status = trimResponse.Substring(0, 1); // Take the first character of the string.
            if (status == "1") HeatersEnabled = true; // Heater Output is on
            else HeatersEnabled = false; // Heater Output is off
        }
        

        private Thread refreshModeThread;
        private bool refreshModeCancelFlag;
        private Object refreshModeLock;
        private bool refreshModeActive = false;
        private bool refreshAtRoomTemperature;
        private bool refreshModeHeaterTurnOffDateTimeFlag = false;
        private bool refreshModeCryoTurnOnDateTimeFlag = false;
        private double refreshTemperature;
        private bool HeatersEnabled;

        /// <summary>
        /// When refresh mode starts or stops, various controls in the user interface need to be disabled or enabled so that the user doesn't accidently do something that will interfere with refresh mode. This function can be used to disable or enable the relevant UI components.
        /// </summary>
        /// <param name="StartStop"></param>
        internal void RefreshModeEnableUIElements(bool StartStop) // Start = true (elements to enable/disable when starting refresh mode)
        {
            // Ugly, but it works...
            // Disable and enable to Start and Stop buttons respectively
            window.EnableControl(window.btStartRefreshMode, !StartStop); // window.btStartRefreshMode.Enabled = false when starting refresh mode (for example)
            window.EnableControl(window.btCancelRefreshMode, StartStop); // window.btCancelRefreshMode.Enabled = true when starting refresh mode (for example)
            // Disable user control of the cryo on/off status
            window.EnableControl(window.checkBoxCryoEnable, !StartStop);
            // Disable user control of the heater setpoint when in refresh mode
            window.EnableControl(window.btUpdateHeaterControlStage2, !StartStop);
            window.EnableControl(window.btUpdateHeaterControlStage1, !StartStop);
            window.EnableControl(window.btHeatersTurnOffWaitStart, !StartStop);
            // Disable clear pressure/temp data so that the user can't interfere with refresh mode
            window.EnableControl(window.btClearAllPressureData, !StartStop);
            window.EnableControl(window.btClearAllTempData, !StartStop);
            window.EnableControl(window.btClearSourcePressureData, !StartStop);
            window.EnableControl(window.btClearBeamlinePressureData, !StartStop);
            window.EnableControl(window.btClearCellTempData, !StartStop);
            window.EnableControl(window.btClearSF6TempData, !StartStop);
            window.EnableControl(window.btClearS2TempData, !StartStop);
            window.EnableControl(window.btClearS1TempData, !StartStop);
            window.EnableControl(window.btClearNeonTempData, !StartStop);
        }

        internal void StartRefreshMode()
        {
            if (refreshModeHeaterTurnOffDateTimeFlag)
            {
                if(refreshModeCryoTurnOnDateTimeFlag)
                {
                    if (window.dateTimePickerStopHeatingAndTurnCryoOn.Value >= window.dateTimePickerRefreshModeTurnHeatersOff.Value)// The heaters can be stopped before or at the same time as the cryo turning on
                    {
                        if (window.dateTimePickerStopHeatingAndTurnCryoOn.Value > DateTime.Now) // The cryo cannot be turned on in the past - otherwise you should just turn on the cryo (instead of using refresh mode)
                        {
                            if (window.dateTimePickerRefreshModeTurnHeatersOff.Value > DateTime.Now) // The heaters cannot be turned off in the past - otherwise you should just turn off the heaters (instead of using refresh mode)
                            {
                                refreshModeThread = new Thread(new ThreadStart(refreshModeWorker));
                                RefreshModeEnableUIElements(true);
                                refreshModeLock = new Object();
                                refreshModeCancelFlag = false;
                                refreshModeActive = true;
                                refreshModeThread.Start();
                                UpdateRefreshTemperature();
                            }
                            else
                            {
                                MessageBox.Show("User has requested that the heaters turn off... in the past. Adjust the datetimes to sensible values.\n\nRefresh mode not started.\n\nDo these messages come across as passive agressive? I hope not...", "Refresh Mode Exception", MessageBoxButtons.OK);
                            }
                        }
                        else
                        {
                            MessageBox.Show("User has requested that the cryo turns on... in the past. Adjust the datetimes to sensible values.\n\nRefresh mode not started.", "Refresh Mode Exception", MessageBoxButtons.OK);
                        }
                    }
                    else
                    {
                        MessageBox.Show("User has requested that the cryo turns on before the heaters turn off - adjust the datetimes to sensible values.\n\nRefresh mode not started.", "Refresh Mode Exception", MessageBoxButtons.OK);
                    }
                }
                else
                {
                    MessageBox.Show("No datetime specified for turning on the cryo.\n\nRefresh mode not started.", "Refresh Mode Exception", MessageBoxButtons.OK);
                }
            }
            else
            {
                MessageBox.Show("No datetime specified for turning off the heaters.\n\nRefresh mode not started.", "Refresh Mode Exception", MessageBoxButtons.OK);
            }
        }
        internal void CancelRefreshMode()
        {
            refreshModeCancelFlag = true;
        }
        private void refreshModeWorker()
        {
            if (!refreshModeCancelFlag) InitializeSourceRefreshWithoutLakeShore();
            if (!refreshModeCancelFlag) WarmUpSourceWithoutLakeShore();
            if (!refreshModeCancelFlag) RefreshModeWait();// Wait at desired temperature, until the user defined datetime
            if (!refreshModeCancelFlag) CoolDownSourceWithoutLakeShore();
            if (refreshModeCancelFlag)
            {
                window.SetTextBox(window.tbRefreshModeStatus, "Refresh mode cancelled");
                StopStage1DigitalHeaterControl(); // turn heaters setpoint loop off
                StopStage2DigitalHeaterControl(); // turn heaters setpoint loop off
                EnableDigitalHeaters(1, false); // turn heaters off
                EnableDigitalHeaters(2, false); // turn heaters off
            }
            RefreshModeEnableUIElements(false);
            refreshModeActive = false;
        }


        public static class SourceRefreshConstants 
        {
            public static Double TurbomolecularPumpUpperPressureLimit { get { return 0.0008; } } // 8e-4 mbar
            public static Double NeonEvaporationCycleTemperatureMax { get { return 30; } }  // Kelvin
            public static Int16 S1LakeShoreHeaterOutput { get { return 3; } }  // 
            public static Int16 S2LakeShoreHeaterOutput { get { return 4; } }  // 
            public static Double TemperatureSetpointDecrementValue { get { return 0.5; } } // Kelvin
            public static Double TemperatureSetpointIncrementValue { get { return 0.5; } } // Kelvin
            public static Int32 NeonEvaporationCycleWaitTime { get { return 5000; } } // milli seconds
            public static Double CryoStartingPressure { get { return 0.00005; } } // 5e-5 mbar
            public static Double CryoStoppingPressure { get { return 0.00001; } } // 1e-5 mbar
            public static Double CryoStartingTemperatureMax { get { return 310; } } // Kelvin
            public static Double RefreshingTemperature { get { return 300; } } // Kelvin
            public static Int32 WarmupMonitoringWait { get { return 3000; } } // milli seconds
            public static Int32 CoolDownWait { get { return 3000; } } // milli seconds
            public static Double NeonEvaporationPollPeriod { get { return 100; } } // milli seconds
        }

        public void EnableRefreshModeRoomTemperature(bool Enable)
        {
            refreshAtRoomTemperature = Enable;
        }
        public void HeaterTurnOffDateTimeSpecified()
        {
            refreshModeHeaterTurnOffDateTimeFlag = true;
        }
        public void CryoTurnOnDateTimeSpecified()
        {
            refreshModeCryoTurnOnDateTimeFlag = true;
        }
        public void UpdateRefreshTemperature()
        {
            string RefreshTemperatureInput = window.tbRefreshModeTemperatureSetpoint.Text;
            double parseddouble;
            if (Double.TryParse(RefreshTemperatureInput, out parseddouble))
            {
                refreshTemperature = parseddouble;
            }
            else MessageBox.Show("Unable to parse refresh temperature string. Ensure that a number has been written, with no additional non-numeric characters.", "", MessageBoxButtons.OK);
        }
        public void UpdateUITimeLeftIndicators()
        {
            TimeSpan TimeLeftUntilCryoTurnsOn = window.dateTimePickerStopHeatingAndTurnCryoOn.Value - DateTime.Now; // Update user interface with the time left until the cryo turns off.
            window.SetTextBox(window.tbHowLongUntilCryoTurnsOn, TimeLeftUntilCryoTurnsOn.ToString(@"d\.hh\:mm\:ss")); // Update textbox to inform user how long is left until the heating process will be forced to stop early and the cryo turned on
            TimeSpan TimeLeftUntilHeatersTurnOff = window.dateTimePickerRefreshModeTurnHeatersOff.Value - DateTime.Now; // Update user interface with the time left until the cryo turns off.
            window.SetTextBox(window.tbRefreshModeHowLongUntilHeatersTurnOff, TimeLeftUntilHeatersTurnOff.ToString(@"d\.hh\:mm\:ss")); // Update textbox to inform user how long is left until the heating process will be forced to stop early and the cryo turned on
        }

        private void InitializeSourceRefresh() 
        {
            window.SetTextBox(window.tbRefreshModeStatus, "Starting initialization process");
        }
        private void InitializeSourceRefreshWithoutLakeShore()
        {
            window.SetTextBox(window.tbRefreshModeStatus, "Starting initialization process");
        }

        /// <summary>
        /// Controls the process of incrementally warming up the source. This is done gradually so that the neon evaporates at a steady rate - reducing the risk to the turbomolecular pump.
        /// A process map (flow diagram) of this code can be found on OneNote in "Equipment + Apparatus" > "Hardware Controller" > "Source refresh mode process maps"
        /// </summary>
        private void EvaporateAndPumpNeon()
        {
            if (!refreshModeCancelFlag)
            {
                window.SetTextBox(window.tbHeaterTempSetpointStage2, SourceRefreshConstants.NeonEvaporationCycleTemperatureMax.ToString());
                UpdateStage2TemperatureSetpoint();
                window.SetTextBox(window.tbHeaterTempSetpointStage1, SourceRefreshConstants.NeonEvaporationCycleTemperatureMax.ToString());
                UpdateStage1TemperatureSetpoint();
                window.SetTextBox(window.tbRefreshModeStatus, "Starting neon evaporation cycle");
            }

            for (; ; )// for (; ; ) is an infinite loop, equivalent to while(true)
            {
                if (refreshModeCancelFlag) break; // Immediately break this for loop if the user has requested that refresh mode be cancelled
                UpdateUITimeLeftIndicators();
                if (lastSourcePressure >= SourceRefreshConstants.TurbomolecularPumpUpperPressureLimit) // If the pressure is too high, then the heaters should be disabled so that the turbomolecular pump is not damaged
                {
                    window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: pressure above turbo limit");
                    if (Stage1HeaterControlFlag & Stage2HeaterControlFlag)
                    {
                        EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S1LakeShoreHeaterOutput, false); // turn off heaters
                        EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S2LakeShoreHeaterOutput, false); // turn off heaters
                    }
                }
                else
                {
                    EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S1LakeShoreHeaterOutput, true); // turn on heaters
                    EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S2LakeShoreHeaterOutput, true); // turn on heaters
                    if (Double.Parse(lastS2Temp) >= SourceRefreshConstants.NeonEvaporationCycleTemperatureMax) // Check if the S2 temperature has reached the end of the neon evaporation cycle (there should be little neon left to evaporate after S2 temperature = NeonEvaporationCycleTemperatureMax)
                    {
                        if (lastSourcePressure <= SourceRefreshConstants.CryoStoppingPressure) // If the pressure is low enough that the cryo cooler can be turned off, then break the for loop.
                        {
                            break;
                        }
                        window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: temperature high enough, but pressure too high for cryo shutdown");
                    }
                    else
                    {
                        window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: pressure and temperature low - heating source");
                    }
                }

                Thread.Sleep(SourceRefreshConstants.NeonEvaporationCycleWaitTime);
            }
        }
        private void EvaporateAndPumpNeonWithoutLakeShore()
        {
            if (!refreshModeCancelFlag)
            {
                window.SetTextBox(window.tbHeaterTempSetpointStage2, SourceRefreshConstants.NeonEvaporationCycleTemperatureMax.ToString());
                UpdateStage2TemperatureSetpoint();
                window.SetTextBox(window.tbHeaterTempSetpointStage1, SourceRefreshConstants.NeonEvaporationCycleTemperatureMax.ToString());
                UpdateStage1TemperatureSetpoint();
                window.SetTextBox(window.tbRefreshModeStatus, "Starting neon evaporation cycle");
            }

            for (; ; )// for (; ; ) is an infinite loop, equivalent to while(true)
            {
                if (refreshModeCancelFlag) break; // Immediately break this for loop if the user has requested that refresh mode be cancelled
                UpdateUITimeLeftIndicators();
                if (lastSourcePressure >= SourceRefreshConstants.TurbomolecularPumpUpperPressureLimit) // If the pressure is too high, then the heaters should be disabled so that the turbomolecular pump is not damaged
                {
                    window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: pressure above turbo limit");
                    if (Stage1HeaterControlFlag & Stage2HeaterControlFlag)
                    {
                        StopStage1DigitalHeaterControl(); // turn heaters setpoint loop off 
                        StopStage2DigitalHeaterControl(); // turn heaters setpoint loop off
                        EnableDigitalHeaters(1, false); // turn heaters off (when stopped, the setpoint loop will leave the heaters in their last enabled/disabled state)
                        EnableDigitalHeaters(2, false); // turn heaters off (when stopped, the setpoint loop will leave the heaters in their last enabled/disabled state)
                    }
                }
                else
                {
                    StartStage1DigitalHeaterControl(); // turn heaters setpoint loop on
                    StartStage2DigitalHeaterControl(); // turn heaters setpoint loop on
                    if (Double.Parse(lastS2Temp) >= SourceRefreshConstants.NeonEvaporationCycleTemperatureMax) // Check if the S2 temperature has reached the end of the neon evaporation cycle (there should be little neon left to evaporate after S2 temperature = NeonEvaporationCycleTemperatureMax)
                    {
                        if (lastSourcePressure <= SourceRefreshConstants.CryoStoppingPressure) // If the pressure is low enough that the cryo cooler can be turned off, then break the for loop.
                        {
                            break;
                        }
                        window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: temperature high enough, but pressure too high for cryo shutdown");
                    }
                    else
                    {
                        window.SetTextBox(window.tbRefreshModeStatus, "Neon evaporation cycle: pressure and temperature low - heating source");
                    }
                }
                
                Thread.Sleep(SourceRefreshConstants.NeonEvaporationCycleWaitTime);
            }
        }

        private void TurnOffCryoAndWarmup()
        {
            TurnOffCryoCooler(); // The pressure should be checked before this function is used (see process maps on OneNote)

            SetHeaterSetpoint(SourceRefreshConstants.S2LakeShoreHeaterOutput, SourceRefreshConstants.RefreshingTemperature);

            for(; ; )
            {
                if(lastSourcePressure < SourceRefreshConstants.TurbomolecularPumpUpperPressureLimit)
                {
                    if(!HeatersEnabled) // if heaters turned off then turn them on
                    {
                        EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S2LakeShoreHeaterOutput, true); // turn on heaters
                    }
                    if (Double.Parse(lastCellTemp) >= SourceRefreshConstants.RefreshingTemperature)
                    {
                        break;
                    }
                }
                else
                {
                    if (HeatersEnabled) // if heaters are on
                    {
                        EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S2LakeShoreHeaterOutput, false); // turn heaters off
                    }
                }
                Thread.Sleep(SourceRefreshConstants.WarmupMonitoringWait);
            }

        }
        private void TurnOffCryoAndWarmupWithoutLakeShore()
        {
            if (!refreshModeCancelFlag) // If refresh mode has been cancelled then skip these functions
            {
                EnableCryoDigitalControl(false); // The pressure should be checked before this function is used (see process maps on OneNote)
                window.SetTextBox(window.tbHeaterTempSetpointStage2, refreshTemperature.ToString());
                UpdateStage2TemperatureSetpoint();
                window.SetTextBox(window.tbHeaterTempSetpointStage1, refreshTemperature.ToString());
                UpdateStage1TemperatureSetpoint();
                window.SetTextBox(window.tbRefreshModeStatus, "Starting warmup");
            }

            // Monitor the pressure as the source heats up. If the pressure gets too high for the turbo, then turn off the heaters. If pressure is low enough, then turn on the heaters.
            for (; ; )
            {
                if (refreshModeCancelFlag) break; // If refresh mode has been cancelled then exit this loop (check on every iteration of the loop)
                UpdateUITimeLeftIndicators();
                if (window.dateTimePickerStopHeatingAndTurnCryoOn.Value < DateTime.Now) break; // If the user requested that the cryo turns off before the temperature has reached the specified value, then exit this loop.
                
                if (lastSourcePressure < SourceRefreshConstants.TurbomolecularPumpUpperPressureLimit)
                {
                    if (!Stage1HeaterControlFlag | !Stage2HeaterControlFlag) // if heaters turned off then turn them on
                    {
                        StartStage1DigitalHeaterControl(); // turn heaters setpoint loop on
                        StartStage2DigitalHeaterControl(); // turn heaters setpoint loop on
                        EnableDigitalHeaters(1, true); // turn heaters on
                        EnableDigitalHeaters(2, true); // turn heaters on
                    }
                    if (Double.Parse(lastS2Temp) >= refreshTemperature) // If the source has reached the desired temperature, then break the loop
                    {
                        break;
                    }
                }
                else
                {
                    if (Stage1HeaterControlFlag | Stage2HeaterControlFlag) // if heaters are on
                    {
                        StopStage1DigitalHeaterControl(); // turn heaters setpoint loop off
                        StopStage2DigitalHeaterControl(); // turn heaters setpoint loop off
                        EnableDigitalHeaters(1, false); // turn heaters off
                        EnableDigitalHeaters(2, false); // turn heaters off
                    }
                }
                Thread.Sleep(SourceRefreshConstants.WarmupMonitoringWait); // Iterate the loop according to this time interval
                window.SetTextBox(window.tbRefreshModeStatus, "Still warming"); // Update refresh mode status textbox
            }
        }


        /// <summary>
        /// Incrementally warms up the source so that the turbomolecular pump isn't at risk of large quantities of neon from being evaporated quickly. 
        /// Once the source is warm enough, the risk becomes low and the cryo can be turned off.
        /// </summary>
        private void WarmUpSource()
        {
            EvaporateAndPumpNeon();
            TurnOffCryoAndWarmup();
        }
        private void WarmUpSourceWithoutLakeShore()
        {
            if (!refreshModeCancelFlag) EvaporateAndPumpNeonWithoutLakeShore();
            if (!refreshModeCancelFlag) TurnOffCryoAndWarmupWithoutLakeShore();
        }
        
        public void RefreshModeWait()
        {
            //If the source reaches the desired temperature before the user defined cryo turn off time, then wait until this time.
            for (; ; )
            {
                if (refreshModeCancelFlag) break; // If refresh mode has been cancelled then exit this loop (check on every iteration of the loop)
                if (window.dateTimePickerStopHeatingAndTurnCryoOn.Value < DateTime.Now) break; // If the user requested that the cryo turns off before the temperature has reached the specified value, then exit this loop.
                if (window.dateTimePickerRefreshModeTurnHeatersOff.Value < DateTime.Now) break; // Break loop when the user defined datetime is reached

                if (refreshAtRoomTemperature) // If the user has stated that the source should bake (maintain user defined temperature whilst cryo is off)
                {
                    if (Stage1HeaterControlFlag | Stage2HeaterControlFlag) // if heaters are on, turn them off
                    {
                        StopStage1DigitalHeaterControl(); // turn heaters setpoint loop off
                        StopStage2DigitalHeaterControl(); // turn heaters setpoint loop off
                        EnableDigitalHeaters(1, false); // turn heaters off
                        EnableDigitalHeaters(2, false); // turn heaters off
                        window.SetTextBox(window.tbRefreshModeStatus, "Waiting at room temperature"); // Update refresh mode status textbox
                    }
                }
                else
                {
                    if (!Stage1HeaterControlFlag | !Stage2HeaterControlFlag) // if heaters turned off, turn them on
                    {
                        StartStage1DigitalHeaterControl(); // turn heaters setpoint loop on
                        StartStage2DigitalHeaterControl(); // turn heaters setpoint loop on
                        EnableDigitalHeaters(1, true); // turn heaters on
                        EnableDigitalHeaters(2, true); // turn heaters on
                        window.SetTextBox(window.tbRefreshModeStatus, "Waiting at desired refreshing temperature"); // Update refresh mode status textbox
                    }
                }

                UpdateUITimeLeftIndicators();
                Thread.Sleep(SourceRefreshConstants.WarmupMonitoringWait); // Iterate the loop according to this time interval
            }
            window.SetTextBox(window.tbHowLongUntilCryoTurnsOn, ""); 
            window.SetTextBox(window.tbRefreshModeHowLongUntilHeatersTurnOff, ""); 
        }

        private void CoolDownSource()
        {
            if(HeatersEnabled)
            {
                EnableLakeGoreHeaterOutput3or4(SourceRefreshConstants.S2LakeShoreHeaterOutput, false);
            }
            for(; ; )
            {
                if(Double.Parse(lastS1Temp) <= SourceRefreshConstants.CryoStartingTemperatureMax & Double.Parse(lastS2Temp) <= SourceRefreshConstants.CryoStartingTemperatureMax)
                { break; }
                Thread.Sleep(SourceRefreshConstants.CoolDownWait);
            }
            for(; ; )
            {
                if(lastSourcePressure <= SourceRefreshConstants.CryoStartingPressure)
                { break; }
                Thread.Sleep(SourceRefreshConstants.CoolDownWait);
            }
            TurnOnCryoCooler();
        }
        private void CoolDownSourceWithoutLakeShore()
        {
            if (!refreshModeCancelFlag)
            {
                StopStage1DigitalHeaterControl(); // turn heaters setpoint loop off
                StopStage2DigitalHeaterControl(); // turn heaters setpoint loop off
                EnableDigitalHeaters(1, false); // turn heaters off
                EnableDigitalHeaters(2, false); // turn heaters off
                window.SetTextBox(window.tbRefreshModeStatus, "Stopping heaters"); // Update refresh mode status textbox
            }
            for(; ; )
            {
                if (refreshModeCancelFlag) break;
                window.SetTextBox(window.tbRefreshModeStatus, "Waiting for temperature to reach the safe operating range for cryo to turn on"); // Update refresh mode status textbox
                if(Double.Parse(lastS1Temp) <= SourceRefreshConstants.CryoStartingTemperatureMax & Double.Parse(lastS2Temp) <= SourceRefreshConstants.CryoStartingTemperatureMax)
                { break; }
                Thread.Sleep(SourceRefreshConstants.CoolDownWait);
            }
            for(; ; )
            {
                if (refreshModeCancelFlag) break;
                window.SetTextBox(window.tbRefreshModeStatus, "Waiting for the pressure to reach a low enough value for the cryo to turn on"); // Update refresh mode status textbox
                if(lastSourcePressure <= SourceRefreshConstants.CryoStartingPressure)
                { break; }
                Thread.Sleep(SourceRefreshConstants.CoolDownWait);
            }
            if (!refreshModeCancelFlag)
            {
                window.SetTextBox(window.tbRefreshModeStatus, "Starting cryo");
                EnableCryoDigitalControl(true);
            }
        }

        #endregion 

        #region Digital heater control

        public bool Stage1HeaterControlFlag;
        public bool Stage2HeaterControlFlag;

        public double Stage1TemperatureSetpoint;
        public double Stage2TemperatureSetpoint;

        public double SetpointVariation = 2;

        public void EnableDigitalHeaters(int Channel, bool Enable)
        {
            if(Channel == 1)
            {
                SetDigitalLine("heatersS1TriggerDigitalOutputTask", Enable);
                window.SetCheckBox(window.checkBoxEnableHeatersS1, Enable);
            }
            else
            {
                if(Channel == 2)
                {
                    SetDigitalLine("heatersS2TriggerDigitalOutputTask", Enable);
                    window.SetCheckBox(window.checkBoxEnableHeatersS2, Enable);
                }
            }
        }

        public void StartStage1DigitalHeaterControl()
        {
            Stage1HeaterControlFlag = true;
            window.EnableControl(window.btStartHeaterControlStage1, false);
            window.EnableControl(window.btStopHeaterControlStage1, true);
            window.EnableControl(window.checkBoxEnableHeatersS1, false);
        }
        public void StartStage2DigitalHeaterControl()
        {
            Stage2HeaterControlFlag = true;
            window.EnableControl(window.btStartHeaterControlStage2, false);
            window.EnableControl(window.btStopHeaterControlStage2, true);
            window.EnableControl(window.checkBoxEnableHeatersS2, false);
        }

        public void StopStage1DigitalHeaterControl()
        {
            Stage1HeaterControlFlag = false; // change control flag so that the temperature setpoint loop stops
            window.EnableControl(window.btStartHeaterControlStage1, true);
            window.EnableControl(window.btStopHeaterControlStage1, false);
            window.EnableControl(window.checkBoxEnableHeatersS1, true);
            EnableDigitalHeaters(1, false); // turn off heater
        }
        public void StopStage2DigitalHeaterControl()
        {
            Stage2HeaterControlFlag = false; // change control flag so that the temperature setpoint loop stops
            window.EnableControl(window.btStartHeaterControlStage2, true);
            window.EnableControl(window.btStopHeaterControlStage2, false);
            window.EnableControl(window.checkBoxEnableHeatersS2, true);
            EnableDigitalHeaters(2, false); // turn off heater
        }

        public void UpdateStage2TemperatureSetpoint()
        {
            Stage2TemperatureSetpoint = Double.Parse(window.tbHeaterTempSetpointStage2.Text);
        }
        public void UpdateStage1TemperatureSetpoint()
        {
            Stage1TemperatureSetpoint = Double.Parse(window.tbHeaterTempSetpointStage1.Text);
        }

        public bool monitorPressureWhenHeating = true;

        public void EnableMonitorPressureWhenHeating(bool Enable)
        {
            monitorPressureWhenHeating = Enable;
        }
        public void ControlHeaters()
        {
            
            if (lastSourcePressure >= SourceRefreshConstants.TurbomolecularPumpUpperPressureLimit & monitorPressureWhenHeating) // If the pressure is too high, then the heaters should be disabled so that the turbomolecular pump is not damaged
            {
                window.SetTextBox(window.tbHeaterControlStatus, "Pressure above safe limit for turbo. Heaters disabled.");
                EnableDigitalHeaters(1, false); // turn heaters off
                EnableDigitalHeaters(2, false); // turn heaters off
            }
            else
            {
                window.SetTextBox(window.tbHeaterControlStatus, "");
                if (Stage2HeaterControlFlag)
                {
                    if (Double.Parse(lastS2Temp) < Stage2TemperatureSetpoint)
                    {
                        EnableDigitalHeaters(2, true);
                    }
                    else EnableDigitalHeaters(2, false);
                }
                if (Stage1HeaterControlFlag)
                {
                    if (Double.Parse(lastS1Temp) < Stage1TemperatureSetpoint)
                    {
                        EnableDigitalHeaters(1, true);
                    }
                    else EnableDigitalHeaters(1, false);
                }
                
            }
        }

        // Turn off heaters thread.
        // This allows the user to define a time at which the heaters will be turned off. 
        // This can be used to heat the source a little whilst away for lunch - evaporating neon while you eat!
        private Thread turnHeatersOffWaitThread;
        private int turnHeatersOffWaitPeriod = 1000;
        private bool turnHeatersOffCancelFlag;
        private Object turnHeatersOffWaitLock;

        internal void StartTurnHeatersOffWait()
        {
            turnHeatersOffWaitThread = new Thread(new ThreadStart(turnHeatersOffWaitWorker));
            window.EnableControl(window.btHeatersTurnOffWaitStart, false);
            window.EnableControl(window.btHeatersTurnOffWaitCancel, true);
            window.EnableControl(window.checkBoxCryoEnable, false);
            turnHeatersOffWaitLock = new Object();
            turnHeatersOffCancelFlag = false;
            turnHeatersOffWaitThread.Start();
        }
        internal void CancelTurnHeatersOffWait()
        {
            turnHeatersOffCancelFlag = true;
        }
        private void turnHeatersOffWaitWorker()
        {
            for (; ; )// for (; ; ) is an infinite loop, equivalent to while(true)
            {
                Thread.Sleep(turnHeatersOffWaitPeriod);
                if (turnHeatersOffCancelFlag)
                {
                    break;
                }
                if (window.dateTimePickerHeatersTurnOff.Value < DateTime.Now)
                {
                    StopStage2DigitalHeaterControl();
                    StopStage1DigitalHeaterControl();
                    EnableDigitalHeaters(1,false);
                    EnableDigitalHeaters(2,false);
                    break;
                }
                TimeSpan TimeLeft = window.dateTimePickerHeatersTurnOff.Value - DateTime.Now;
                window.SetTextBox(window.tbHowLongUntilHeatersTurnOff, TimeLeft.ToString(@"d\.hh\:mm\:ss"));
            }
            window.EnableControl(window.btHeatersTurnOffWaitStart, true);
            window.EnableControl(window.btHeatersTurnOffWaitCancel, false);
            window.SetTextBox(window.tbHowLongUntilHeatersTurnOff, "");
        }

        #endregion

        #region Pressure monitor

        private double lastSourcePressure;
        private int pressureMovingAverageSampleLength = 10;
        private Queue<double> pressureSamples = new Queue<double>();
        private string sourceSeries = "Source Pressure";

        public void UpdatePressureMonitor()
        {
            //sample the pressure
            lastSourcePressure = sourcePressureMonitor.Pressure;

            //add samples to Queues for averaging
            pressureSamples.Enqueue(lastSourcePressure);

            //drop samples when array is larger than the moving average sample length
            while (pressureSamples.Count > pressureMovingAverageSampleLength)
            {
                pressureSamples.Dequeue();
            }

            //average samples
            double avgPressure = pressureSamples.Average();
            string avgPressureExpForm = avgPressure.ToString("E");

            //update text boxes
            window.SetTextBox(window.tbPSource, (avgPressureExpForm).ToString());
        }

        public void ClearPressureMonitorAv()
        {
            pressureSamples.Clear();
        }

        public void PlotLastPressure()
        {
            //sample the pressure
            lastSourcePressure = sourcePressureMonitor.Pressure;
            DateTime localDate = DateTime.Now;

            //plot the most recent samples
            window.AddPointToChart(window.chart1, sourceSeries, localDate, lastSourcePressure);
        }

        private JSONSerializer pressureDataSerializer;
        public void StartLoggingPressure()
        {
            pressureDataSerializer = new JSONSerializer();
            Console.Write("here1");
            string initialDataDir = Environs.FileSystem.GetDataDirectory((String)Environs.FileSystem.Paths["HardwareControllerDataPath"]);
            //string currentYear = DateTime.Now.Year.ToString();
            //string currentMonth = DateTime.Now.Month.ToString();
            //string currentDay = DateTime.Now.Day.ToString();
            Console.Write("here2");

            pressureDataSerializer.StartLogFile(initialDataDir + "Temperature and Pressure Records\\Pressure\\" +
                Environs.FileSystem.GenerateNextDataFileName() + ".json");
            pressureDataSerializer.StartProcessingData();
        }
        public void StopLoggingPressure()
        {
            pressureDataSerializer.EndLogFile();
        }

        # endregion

        #region Temperature Monitors

        private string receivedData;
        private int TempMovingAverageSampleLength = 2;
        //private Queue<double> TempSamples = new Queue<double>();
        public string[] TemperatureArray;
        public string lastCellTemp;
        public string lastS1Temp;
        public string lastS2Temp;
        public string lastNeonTemp;
        public string lastSF6Temp;
        private string cellTSeries = "Cell Temperature";
        private string S1TSeries = "S1 Temperature";
        private string S2TSeries = "S2 Temperature";
        private string SF6TSeries = "SF6 Temperature";
        private string neonTSeries = "Neon Temperature";

        public void UpdateAllTempMonitors()
        {
            //sample the temperatures
            receivedData = tempController.GetTemperature(0,"K");
            TemperatureArray = receivedData.Split(',');
            if (TemperatureArray.Length == 8)
            {
                lastCellTemp = TemperatureArray[0]; // LakeShore Input A
                lastNeonTemp = TemperatureArray[1]; // LakeShore Input B
                lastS2Temp = TemperatureArray[2];   // LakeShore Input C
                lastSF6Temp = TemperatureArray[3];  // LakeShore Input D1
                lastS1Temp = TemperatureArray[4];   // LakeShore Input D2
                window.SetTextBox(window.tbTCell, lastCellTemp);
                window.SetTextBox(window.tbTNeon, lastNeonTemp);
                window.SetTextBox(window.tbTS2, lastS2Temp);
                window.SetTextBox(window.tbTS1, lastS1Temp);
                window.SetTextBox(window.tbTSF6, lastSF6Temp);
            }
            else
            {
                window.SetTextBox(window.tbTCell, "err_UpdateAllTempMonitors");
                window.SetTextBox(window.tbTNeon, "err_UpdateAllTempMonitors");
                window.SetTextBox(window.tbTS2, "err_UpdateAllTempMonitors");
                window.SetTextBox(window.tbTSF6, "err_UpdateAllTempMonitors");
                window.SetTextBox(window.tbTNeon, "err_UpdateAllTempMonitors");
            }
        }

        // The following function isn't currently being used, but has been kept as a back up for when we don't have the LakeShore temperature controller.
        double[] tempMonitorsData;
        /// <summary>
        /// Function to measure the temperature of silicon diodes via the analogue inputs of our data acquisition system (i.e. when we don't have the LakeShore temperature controller)
        /// </summary>
        public void UpdateAllTempMonitorsUsingDAQ()
        {
            //sample the temperatures
            tempMonitorsData = tempMonitors.Temperature();
            if (tempMonitorsData.Length == 4)
            {

                lastCellTemp = tempMonitorsData[0].ToString("N6");
                lastS1Temp = tempMonitorsData[1].ToString("N6");
                lastS2Temp = tempMonitorsData[2].ToString("N6");
                lastSF6Temp = tempMonitorsData[3].ToString("N6");
                window.SetTextBox(window.tbTCell, lastCellTemp);
                window.SetTextBox(window.tbTNeon, lastS1Temp);
                window.SetTextBox(window.tbTS2, lastS2Temp);
                window.SetTextBox(window.tbTSF6, lastSF6Temp);
            }
            else
            {
                window.SetTextBox(window.tbTCell, "err_UpdateAllTempMonitorsUsingDAQ");
                window.SetTextBox(window.tbTNeon, "err_UpdateAllTempMonitorsUsingDAQ");
                window.SetTextBox(window.tbTS2, "err_UpdateAllTempMonitorsUsingDAQ");
                window.SetTextBox(window.tbTSF6, "err_UpdateAllTempMonitorsUsingDAQ");
            }
        }

        public void PlotLastTemperatures()
        {
            DateTime localDate = DateTime.Now;
            double CellTemp = Double.Parse(lastCellTemp);
            double S1Temp = Double.Parse(lastS1Temp);
            double S2Temp = Double.Parse(lastS2Temp);
            double SF6Temp = Double.Parse(lastSF6Temp);
            double NeonTemp = Double.Parse(lastNeonTemp);

            //plot the most recent samples
            window.AddPointToChart(window.chart2, cellTSeries, localDate, CellTemp);
            window.AddPointToChart(window.chart2, S1TSeries, localDate, S1Temp);
            window.AddPointToChart(window.chart2, S2TSeries, localDate, S2Temp);
            window.AddPointToChart(window.chart2, SF6TSeries, localDate, SF6Temp);
            window.AddPointToChart(window.chart2, neonTSeries, localDate, NeonTemp);
        }

        #endregion

        #region Temperature and Pressure Monitoring
        // If the LakeShore is not in operation, the silicon diodes can still be monitored by measuring the voltage drop across them (which is temperature dependent).

        private Thread PTMonitorPollThread;
        private int PTMonitorPollPeriod = 1000;
        private int PTMonitorPollPeriodLowerLimit = 100;
        private bool PTMonitorFlag;
        private Object PTMonitorLock;
        public string csvDataTemperatureAndPressure = "";

        /// <summary>
        /// Many user interface (UI) components need to be enabled/disabled so that the user can't perform actions that could be harmful to the experiment. This function combines this list of UI elements.
        /// </summary>
        /// <param name="StartStop"></param>
        internal void PTMonitorPollEnableUIElements(bool StartStop) // Start = true (elements to enable/disable when starting refresh mode)
        {
            window.EnableControl(window.btStartTandPMonitoring, !StartStop); // window.btStartTandPMonitoring.Enabled = false when starting refresh mode (for example)
            window.EnableControl(window.btStopTandPMonitoring, StartStop); // window.btStopTandPMonitoring.Enabled = true when starting refresh mode (for example)
            window.EnableControl(window.checkBoxSF6TempPlot, StartStop);
            window.EnableControl(window.checkBoxS2TempPlot, StartStop);
            window.EnableControl(window.checkBoxS1TempPlot, StartStop);
            window.EnableControl(window.checkBoxCellTempPlot, StartStop);
            window.EnableControl(window.checkBoxBeamlinePressurePlot, StartStop);
            window.EnableControl(window.checkBoxSourcePressurePlot, StartStop);
            window.EnableControl(window.btUpdateHeaterControlStage2, StartStop);
            window.EnableControl(window.btStartHeaterControlStage2, StartStop);
            window.EnableControl(window.btStartHeaterControlStage1, StartStop);
            window.EnableControl(window.btUpdateHeaterControlStage1, StartStop);
            window.EnableControl(window.btStartRefreshMode, StartStop);

        }

        public void UpdatePTMonitorPollPeriod()
        {
            int PTMonitorPollPeriodParseValue;
            if (Int32.TryParse(window.tbTandPPollPeriod.Text, out PTMonitorPollPeriodParseValue))
            {
                if (PTMonitorPollPeriodParseValue >= PTMonitorPollPeriodLowerLimit)
                {
                    PTMonitorPollPeriod = PTMonitorPollPeriodParseValue; // Update PT monitoring poll period
                }
                else MessageBox.Show("Poll period value too small. The temperature and pressure can only be polled every " + PTMonitorPollPeriodLowerLimit.ToString() + " ms. The limiting factor is communication with the LakeShore temperature controller.", "User input exception", MessageBoxButtons.OK);
            }
            else MessageBox.Show("Unable to parse setpoint string. Ensure that an integer number has been written, with no additional non-numeric characters.", "", MessageBoxButtons.OK);
        }

        internal void StartPTMonitorPoll()
        {
            PTMonitorPollThread = new Thread(() =>
            {
                PTMonitorPollWorker();
            });
            PTMonitorPollThread.IsBackground = true; // When the application is closed, this thread will also immediately stop. This is lazy coding, but it works and shouldn't cause any problems. This means it is a background thread of the main (UI) thread, so it will end with the main thread.
            
            pressureMovingAverageSampleLength = 10; 
            Stage2HeaterControlFlag = false;
            Stage1HeaterControlFlag = false;
            UpdateStage1TemperatureSetpoint();
            UpdateStage2TemperatureSetpoint();
            PTMonitorPollEnableUIElements(true);
            if (csvDataTemperatureAndPressure == "") csvDataTemperatureAndPressure += "Unix Time Stamp (ms)" + "," + "Full date/time" + "," + "Cell Temperature (K)" + "," + "S1 Temperature (K)" + "," + "S2 Temperature (K)" + "," + "SF6 Temperature (K)" + "," + "Source Pressure (mbar)" + "," + "Beamline Pressure (mbar)" + "\r\n"; // Header lines for csv file
            pressureSamples.Clear();
            PTMonitorLock = new Object();
            PTMonitorFlag = false;
            PTMonitorPollThread.Start();
        }
        internal void StopPTMonitorPoll()
        {
            if(refreshModeActive)
            {
                MessageBox.Show("Refresh mode is currently active. To stop temperature and pressure monitoring, please first cancel refresh mode and ensure that the apparatus is in a safe state to be left unmonitored.", "Refresh Mode Exception", MessageBoxButtons.OK);
            }
            else
            {
                UEDMSavePlotDataDialog savePTDataDialog = new UEDMSavePlotDataDialog("Save data message", "Would you like to save the temperature and pressure data now? \n\nThe data will not be cleared.");
                savePTDataDialog.ShowDialog();
                if (savePTDataDialog.DialogResult != DialogResult.Cancel)
                {
                    StopStage1DigitalHeaterControl();
                    StopStage2DigitalHeaterControl();
                    EnableDigitalHeaters(1, false);
                    EnableDigitalHeaters(2, false);
                    PTMonitorFlag = true;
                    if (savePTDataDialog.DialogResult == DialogResult.Yes)
                    {
                        SavePlotDataToCSV(csvDataTemperatureAndPressure);
                    }
                }
                savePTDataDialog.Dispose();
            }
        }
        private void PTMonitorPollWorker()
        {
            int count = 0;

            for (; ; )// for (; ; ) is an infinite loop, equivalent to while(true)
            {
                Thread.Sleep(PTMonitorPollPeriod);
                ++count;
                lock (PTMonitorLock)
                {
                    UpdateAllTempMonitors(); 
                    PlotLastTemperatures();
                    UpdatePressureMonitor();
                    PlotLastPressure();

                    Double unixTimestamp = (Double)(DateTime.Now.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
                    string csvLine = unixTimestamp + "," + DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss.fff tt") + "," + lastCellTemp + "," + lastS1Temp + "," + lastS2Temp + "," + lastSF6Temp + "," + lastSourcePressure + "\r\n";
                    csvDataTemperatureAndPressure += csvLine;

                    ControlHeaters();
                    if (PTMonitorFlag)
                    {
                        PTMonitorFlag = false;
                        break;
                    }
                }
            }
            PTMonitorPollEnableUIElements(false);
        }

        #endregion

        #region Plotting functions

        public void ChangePlotYAxisScale(int ChartNumber)
        {
            if(ChartNumber == 1)
            {
                string YScale = window.comboBoxPlot1ScaleY.Text; // Read the Y scale mode chosen by the user in the UI
                window.ChangeChartYScale(window.chart1, YScale);
                window.SetAxisYIsStartedFromZero(window.chart1, false);
            }
            else
            {
                if(ChartNumber == 2)
                {
                    string YScale = window.comboBoxPlot2ScaleY.Text; // Read the Y scale mode chosen by the user in the UI
                    window.ChangeChartYScale(window.chart2, YScale);
                    window.SetAxisYIsStartedFromZero(window.chart2, false);
                }
            }
            
        }

        /// <summary>
        /// Will enable or disable a data set in a specified plot. This allows the user to choose which data to plot on a chart.
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="series"></param>
        /// <param name="enable"></param>
        public void EnableChartSeries(Chart chart, string series, bool enable)
        {
            window.EnableChartSeries(chart, series, enable);
        }

        /// <summary>
        ///  Will delete the plot data for a given series (in a given chart/plot)
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="series"></param>
        public void ClearChartSeriesData(Chart chart, string series)
        {
            window.ClearChartSeriesData(chart, series);
        }

        #endregion

        #region Neon Flow Controller

        private double lastNeonFlowAct;
        private double lastNeonFlowSetpoint;
        private double newNeonFlowSetpoint;
        private string neonFlowActSeries = "Neon Flow";
        private string neonFlowChannelNumber = "2"; // Channel number on the MKS PR4000B flow controller
        private double neonFlowUpperLimit = 100; // Maximum neon flow that the MKS PR4000B flow controller is capable of.
        private double neonFlowLowerLimit = 0; // Minimum neon flow that the MKS PR4000B flow controller is capable of.

        public void UpdateNeonFlowActMonitor()
        {
            //sample the neon flow (actual)
            lastNeonFlowAct = neonFlowController.QueryActualValue(neonFlowChannelNumber);

            //update text boxes
            window.SetTextBox(window.tbNeonFlowActual, lastNeonFlowAct.ToString("N3"));
        }

        public void UpdateNeonFlowSetpointMonitor()
        {
            //sample the neon flow (actual)
            lastNeonFlowSetpoint = neonFlowController.QuerySetpoint(neonFlowChannelNumber);

            //update text boxes
            window.SetTextBox(window.tbNeonFlowSetpoint, lastNeonFlowSetpoint.ToString("N3"));
        }

        public void PlotLastNeonFlowAct()
        {
            DateTime localDate = DateTime.Now;

            //plot the most recent sample
            window.AddPointToChart(window.chart3, neonFlowActSeries, localDate, lastNeonFlowAct);
        }


        private Thread NeonFlowMonitorPollThread;
        private int NeonFlowMonitorPollPeriod = 1000;
        private bool NeonFlowMonitorFlag;
        private bool NeonFlowSetPointFlag;
        private Object NeonFlowMonitorLock;

        internal void StartNeonFlowMonitorPoll()
        {
            NeonFlowMonitorPollThread = new Thread(new ThreadStart(NeonFlowActMonitorPollWorker));
            NeonFlowMonitorPollThread.IsBackground = true; // When the application is closed, this thread will also immediately stop. This is lazy coding, but it works and shouldnn't cause any problems. This means it is a background thread of the main (UI) thread, so it will end with the main thread.
            NeonFlowMonitorPollPeriod = Int32.Parse(window.tbNeonFlowActPollPeriod.Text);
            window.EnableControl(window.btStartNeonFlowActMonitor, false);
            window.EnableControl(window.btStopNeonFlowActMonitor, true);
            window.EnableControl(window.tbNewNeonFlowSetPoint, true);
            window.EnableControl(window.btSetNewNeonFlowSetpoint, true);
            NeonFlowMonitorLock = new Object();
            NeonFlowMonitorFlag = false;
            NeonFlowSetPointFlag = false;
            NeonFlowMonitorPollThread.Start();
        }

        internal void StopNeonFlowMonitorPoll()
        {
            NeonFlowMonitorFlag = true;
        }

        private void NeonFlowActMonitorPollWorker()
        {
            for (; ; )// for (; ; ) is an infinite loop, equivalent to while(true)
            {
                Thread.Sleep(NeonFlowMonitorPollPeriod);
                lock (NeonFlowMonitorLock)
                {
                    UpdateNeonFlowActMonitor();
                    PlotLastNeonFlowAct();
                    UpdateNeonFlowSetpointMonitor();
                    if (NeonFlowSetPointFlag)
                    {
                        neonFlowController.SetSetpoint(neonFlowChannelNumber, newNeonFlowSetpoint.ToString());
                        NeonFlowSetPointFlag = false;
                    }
                    if (NeonFlowMonitorFlag)
                    {
                        NeonFlowMonitorFlag = false;
                        break;
                    }
                }
            }
            window.EnableControl(window.btStartNeonFlowActMonitor, true);
            window.EnableControl(window.btStopNeonFlowActMonitor, false);
            window.EnableControl(window.tbNewNeonFlowSetPoint, false);
            window.EnableControl(window.btSetNewNeonFlowSetpoint, false);
        }

        public void SetNeonFlowSetpoint()
        {
            if (Double.TryParse(window.tbNewNeonFlowSetPoint.Text, out newNeonFlowSetpoint))
            {
                if (newNeonFlowSetpoint <= neonFlowUpperLimit & neonFlowLowerLimit <= newNeonFlowSetpoint)
                {
                    NeonFlowSetPointFlag = true; // set flag that will trigger the setpoint to be changed in NeonFlowActMonitorPollWorker()
                }
                else MessageBox.Show("Setpoint request is outside of the MKS PR4000B flow range (" + neonFlowLowerLimit.ToString() + " - " + neonFlowUpperLimit.ToString() + " SCCM)", "User input exception", MessageBoxButtons.OK);
            }
            else MessageBox.Show("Unable to parse setpoint string. Ensure that a number has been written, with no additional non-numeric characters.", "", MessageBoxButtons.OK);
        }

        #endregion

        #region LakeShore 336
        public string[] PIDValueArray;
        public void QueryPIDLoopValues()
        {
            if (window.comboBoxLakeShore336OutputsQuery.Text == "1" | window.comboBoxLakeShore336OutputsQuery.Text == "2")
            {
                receivedData = tempController.QueryPIDLoopValues(Int32.Parse(window.comboBoxLakeShore336OutputsQuery.Text));
                PIDValueArray = receivedData.Split(',');
                window.SetTextBox(window.tbLakeShore336PIDPValueOutput, PIDValueArray[0]);
                window.SetTextBox(window.tbLakeShore336PIDIValueOutput, PIDValueArray[1]);
                window.SetTextBox(window.tbLakeShore336PIDDValueOutput, PIDValueArray[2]);
            }
            else
            {
                string message = "Please select output 1 or 2";
                string caption = "User input exception";
                MessageBox.Show(message, caption, MessageBoxButtons.OK);
            }
        }

        #endregion
    }
}
