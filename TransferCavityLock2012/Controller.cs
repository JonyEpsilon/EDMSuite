﻿using System;
using System.Threading;
using NationalInstruments.DAQmx;
using DAQ.TransferCavityLock2012;
using DAQ.Environment;
using DAQ.HAL;
using DAQ;
using System.Windows.Forms;
using System.Collections.Generic;

namespace TransferCavityLock2012
{
    /// <summary>
    /// A class for locking the laser using a transfer cavity.
    /// The controller reads the voltage fed to the piezo of the cavity, the voltage from a photodiode measuring the light from a master laser (He-Ne)
    /// and a further n photodiode signals from n lasers you're trying to lock.
    /// The controller then fits a lorenzian to each of these datasets, and works out what to do with each slave laser to keep the peak distances the same.
    /// 
    /// EXAMPLE: In the hardware controller, you need:
    /// Info.Add("TCLLockableLasers", new string[] {"laser", "laser2"});             //Names of lasers you want to lock.
    /// Info.Add("TCLPhotodiodes", new string[] {"cavity", "master", "p1" ,"p2"});   //Names of photodiodes to read from. THE FIRST TWO MUST BE CAVITY AND MASTER PHOTODIODE!!!!
    /// Info.Add("laser", "p1");                                                     //Paring info. This means: the photodiode corresponding to "laser" is "p1".
    /// Info.Add("laser2", "p2");
    /// Careful! If you get the labelling wrong in the hardware control class, all hell breaks loose.
    /// </summary>
    public class Controller : MarshalByRefObject
    {

        #region Declarations

        public const int default_ScanPoints = 200;


        private MainForm ui;
        
        private Dictionary<string, double[]> fits;              //Somewhere to store all the fits
        public Dictionary<string, SlaveLaser> SlaveLasers;      //Stores all the slave laser classes.
        private Dictionary<string, int> aiChannels;             //All ai channels, including the cavity and the master.
        private Dictionary<string, string> lookupAI;            //This is how to tell TCL which photodiode corresponds to which slave laser.

        
        
        TransferCavity2012Lockable tcl;
        
        public enum ControllerState
        {
            STOPPED, RUNNING
        };
        public ControllerState TCLState = ControllerState.STOPPED;
        public object rampStopLock = new object();
        public object tweakLock = new object();


        // without this method, any remote connections to this object will time out after
        // five minutes of inactivity.
        // It just overrides the lifetime lease system completely.
        public override Object InitializeLifetimeService()
        {
            return null;
        }

        #endregion

        #region Initialization

        public void Start()
        {
            ui = new MainForm();
            ui.controller = this;
            
            string[] lockableLasers = (string[])Environs.Hardware.GetInfo("TCLLockableLasers");
            string[] photodiodes = (string[])Environs.Hardware.GetInfo("TCLPhotodiodes");

            initializeSlaveLaserControl(lockableLasers);
            initializeAIs(photodiodes);
            makeLaserToAILookupTable();

            TCLState = ControllerState.STOPPED;
            Application.Run(ui);
        }

        private void initializeSlaveLaserControl(string[] lockableLasers)
        {
            SlaveLasers = new Dictionary<string, SlaveLaser>();
           
            foreach (string s in lockableLasers)
            {
                SlaveLasers.Add(s, new SlaveLaser(s));
                SlaveLasers[s].controller = this;
            }
        }

        private void initializeAIs(string[] channels)
        {
            tcl = new DAQMxTCL2012ExtTriggeredMultiReadHelper(channels, "TCLTrigger");
            aiChannels = new Dictionary<string, int>();
            foreach (string s in channels)
            {
                aiChannels.Add(s, aiChannels.Count);
            }
        }

        private void makeLaserToAILookupTable()
        {
            lookupAI = new Dictionary<string,string>();

            foreach (string s in (string[])Environs.Hardware.GetInfo("TCLLockableLasers"))
            {
                lookupAI.Add(s, (string)Environs.Hardware.GetInfo(s));
            }
        }

        public void InitializeUI()
        {
            ui.SetNumberOfPoints(default_ScanPoints);
            foreach (KeyValuePair<string, SlaveLaser> laser in SlaveLasers)
            {
                ui.AddSlaveLaser(laser.Value.Name);

            }
            ui.ShowAllTabPanels();
            ui.UpdateUIState(TCLState);
        }

        

        #endregion

        #region START AND STOP

        public void StartTCL()
        {
            TCLState = Controller.ControllerState.RUNNING;
            Thread.Sleep(2000);
            Thread mainThread = new Thread(new ThreadStart(mainLoop));
            ui.UpdateUIState(TCLState);
            
            mainThread.Start();
        }

        /// <summary>
        /// This will make the program exit the while loop in ramploop.
        /// Note: this will call 
        /// </summary>
        public void StopTCL()
        {
            TCLState = ControllerState.STOPPED;
            ui.UpdateUIState(TCLState); 
        }
        #endregion

        #region Controlling the state of the UI
        /// <summary>
        /// The controller needs to be able to change the UI. Enabling buttons, changing the values in text boxes...
        /// </summary>
        /// <param name="name"></param>
        /// <param name="state"></param>

        public void UpdateUIState(string name, SlaveLaser.LaserState state)
        {
            ui.UpdateUIState(name, state);
        }
        public void RefreshLockParametersOnUI(string name)
        {
            ui.SetLaserVoltage(name, SlaveLasers[name].VoltageToLaser);
            ui.SetGain(name, SlaveLasers[name].Gain);
            ui.SetLaserSetPoint(name, SlaveLasers[name].LaserSetPoint);
        }

        #endregion

        #region Passing Events from UIs to the correct slave laser class.
        /// <summary>
        /// These are events triggered by one of the LockControlPanels. 
        /// Example: User changes the voltage to be sent to the slave laser.
        /// The UI will call VoltageToLaserChanged. Each LockControlPanel knows which laser it corresponds to. so the "name" argument tells
        /// the controller which SlaveLaser class it needs to send the command to!
        /// </summary>
        /// <param name="name"></param>
        /// <param name="voltage"></param>
        internal void VoltageToLaserChanged(string name, double voltage)
        {
            SlaveLasers[name].VoltageToLaser = voltage;
            SlaveLasers[name].SetLaserVoltage();
        }
        internal void GainChanged(string name, double g)
        {
            SlaveLasers[name].Gain = g;
        }
        internal void SetPointIncrementSize(string name, double value)
        {
            SlaveLasers[name].SetPointIncrementSize = value;
        }
        internal void AddSetPointIncrement(string name)
        {
            SlaveLasers[name].AddSetPointIncrement();
        }
        internal void AddSetPointDecrement(string name)
        {
            SlaveLasers[name].AddSetPointDecrement();
        }
        public void EngageLock(string name)
        {
            SlaveLasers[name].ArmLock();
            ui.UpdateUIState(TCLState);
        }
        public void DisengageLock(string name)
        {
            SlaveLasers[name].DisengageLock();
            ui.UpdateUIState(TCLState);
        }
        #endregion

        #region SCAN! (MAIN PART OF PROGRAM. If reading through code, start here)

        /// <summary>
        /// Reads some analogue inputs.
        /// The basic unit of the program.
        /// </summary>
        private CavityScanData acquireAI(ScanParameters sp)
        {
            CavityScanData scanData = new CavityScanData(sp.Steps, aiChannels, lookupAI); //How many channels we expect. one pd for each slave, the He-Ne and the cavity voltage.
            scanData.AIData = tcl.ReadAI(sp.Steps);
            return scanData;
        }


        /// <summary>
        /// The main loop. Reads the analog inputs, fits to the data and (when locked) adjusts the slave laser voltage.
        /// </summary>
        private void mainLoop()
        {
            fits = new Dictionary<string, double[]>(); //Somewhere to store the fits for an iteration.
            
            readParametersFromUI();                             //This isn't part of the loop. Do an initial setup of the parameters.
            ScanParameters sp = createInitialScanParameters();

            initializeAIHardware(sp);

            CavityScanData scanData;            

            while (TCLState != ControllerState.STOPPED)
            {
                
                scanData = acquireAI(sp);

                if (scanData != null)
                {
                    plotCavity(scanData);

                    fits["masterFits"] = fitMaster(scanData);
                    plotMaster(scanData, fits["masterFits"]);

                    foreach (KeyValuePair<string, SlaveLaser> pair in SlaveLasers)
                    {
                        string slName = pair.Key;
                        SlaveLaser sl = pair.Value;

                        fits[slName + "Fits"] = fitSlave(slName, scanData);
                        plotSlave(slName, scanData, fits[slName + "Fits"]);

                        switch (sl.lState)
                        {
                            case SlaveLaser.LaserState.FREE:
                                break;

                            case SlaveLaser.LaserState.LOCKING:
                                sl.CalculateLaserSetPoint(fits["masterFits"], fits[slName + "Fits"]);
                                sl.Lock();
                                break;

                            case SlaveLaser.LaserState.LOCKED:
                                sl.RefreshLock(fits["masterFits"], fits[slName + "Fits"]);
                                RefreshLockParametersOnUI(sl.Name);
                                break;
                        }
                        
                    }
                    
                }
            }
            endRamping();
        }

        #endregion

        #region Some plotting and fitting stuff

        private double[] fitMaster(CavityScanData data)
        {
            return CavityScanFitHelper.FitLorenzianToData(data.GetCavityData(), data.GetMasterData());
        }
        private void plotMaster(CavityScanData data, double[] MasterFit)
        {
            double[] cavity = data.GetCavityData();
            double[] master = data.GetMasterData();
            ui.DisplayMasterData(cavity, master, CavityScanFitHelper.CreatePointsFromFit(cavity, MasterFit));
        }
        private void plotCavity(CavityScanData data)
        {
            double[] indeces = new double[data.GetCavityData().Length];
            for (int i = 0; i < indeces.Length; i++)
            {
                indeces[i] = i;
            }
            ui.DisplayCavityData(indeces, data.GetCavityData());
        }

        private double[] fitSlave(string name, CavityScanData data)
        {
            return CavityScanFitHelper.FitLorenzianToData(data.GetCavityData(), data.GetSlaveData(name));
        }
        private void plotSlave(string name, CavityScanData data, double[] slaveFit)
        {
            double[] cavity = data.GetCavityData();
            double[] slave = data.GetSlaveData(name);
            ui.DisplaySlaveData(name, cavity, slave, CavityScanFitHelper.CreatePointsFromFit(cavity, slaveFit));
        }


        #endregion

        #region privates
        /// <summary>
        /// Gets some parameters from the UI and stores them on the controller. Mostly redundant now that the code has changed a lot.
        /// </summary>
        private int numberOfPoints;
        private void readParametersFromUI()
        {
            // read out UI params
            numberOfPoints = ui.GetNumberOfPoints();
        }
        

        private ScanParameters createInitialScanParameters()
        {
            ScanParameters sp = new ScanParameters();
            sp.Steps = numberOfPoints;
            return sp;
        }
        /// <summary>
        /// The function which gets called at the end, after breaking out of the while loop.
        /// </summary>
        private void endRamping()
        {
            tcl.DisposeAITask();
            foreach (KeyValuePair<string, SlaveLaser> pair in SlaveLasers)
            {
                string slName = pair.Key;
                SlaveLaser sl = pair.Value;

                if (sl.lState != SlaveLaser.LaserState.FREE)
                {
                    sl.VoltageToLaser = 0.0;
                    sl.DisengageLock();
                    sl.DisposeLaserControl();
                }
            }
        }
        /// <summary>
        /// Prepares the hardware for analog reads.
        /// </summary>
        /// <param name="sp"></param>
        private void initializeAIHardware(ScanParameters sp)
        {
            tcl.ConfigureReadAI(sp.Steps, false);
        }

        #endregion

    }




}
