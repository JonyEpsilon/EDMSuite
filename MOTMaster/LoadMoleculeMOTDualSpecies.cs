﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAQ.Pattern;
using DAQ.Analog;
using DAQ;

namespace MOTMaster.SnippetLibrary
{
    public class LoadMoleculeMOTDualSpecies : MOTMasterScriptSnippet
    {
        public LoadMoleculeMOTDualSpecies(PatternBuilder32 p, Dictionary<String, Object> parameters)
        {
            AddDigitalSnippet(p, parameters);
        }
        public LoadMoleculeMOTDualSpecies(AnalogPatternBuilder p, Dictionary<String, Object> parameters)
        {
            AddAnalogSnippet(p, parameters);
        }


        public void AddDigitalSnippet(PatternBuilder32 p, Dictionary<String, Object> parameters)
        {
            int patternStartBeforeQ = (int)parameters["TCLBlockStart"];
            int slowingChirpStartTime = (int)parameters["SlowingChirpStartTime"];
            int slowingNewDetuningTime = slowingChirpStartTime + (int)parameters["SlowingChirpDuration"];
            int slowingChirpBackTime = slowingNewDetuningTime + (int)parameters["SlowingChirpHoldDuration"];
            int slowingChirpFinishedTime = slowingChirpBackTime + (int)parameters["SlowingChirpDuration"];

            p.Pulse(patternStartBeforeQ, slowingChirpStartTime, slowingChirpFinishedTime - slowingChirpStartTime, "bXLockBlock"); ; // Want it to be blocked for whole time that bX laser is moved
            p.Pulse(0, 0, 10, "aoPatternTrigger");  //THIS TRIGGERS THE ANALOG PATTERN. The analog pattern will start at the same time as the Q-switch is fired.
            p.Pulse(patternStartBeforeQ, -(int)parameters["HeliumShutterToQ"], (int)parameters["HeliumShutterDuration"], "heliumShutter");
            p.Pulse(patternStartBeforeQ, (int)parameters["slowingAOMOnStart"], (int)parameters["slowingAOMOffStart"] - (int)parameters["slowingAOMOnStart"], "bXSlowingAOM"); //first pulse to slowing AOM
            p.Pulse(patternStartBeforeQ, (int)parameters["slowingRepumpAOMOnStart"], (int)parameters["slowingRepumpAOMOffStart"] - (int)parameters["slowingRepumpAOMOnStart"], "v10SlowingAOM"); //first pulse to slowing repump AOM

        }

        public void AddAnalogSnippet(AnalogPatternBuilder p, Dictionary<String, Object> parameters)
        {
            int patternStartBeforeQ = (int)parameters["TCLBlockStart"];
            int slowingChirpStartTime = patternStartBeforeQ + (int)parameters["SlowingChirpStartTime"];
            int slowingNewDetuningTime = slowingChirpStartTime + (int)parameters["SlowingChirpDuration"];
            int slowingChirpBackTime = slowingNewDetuningTime + (int)parameters["SlowingChirpHoldDuration"];
            int slowingChirpFinishedTime = slowingChirpBackTime + (int)parameters["SlowingChirpDuration"];

            p.AddChannel("slowingChirp");
            p.AddChannel("slowingCoilsCurrent");
            p.AddChannel("MOTCoilsCurrent");

            // Slowing Chirp
            p.AddAnalogValue("slowingChirp", 0, (double)parameters["SlowingChirpStartValue"]);
            p.AddLinearRamp("slowingChirp", slowingChirpStartTime, (int)parameters["SlowingChirpDuration"], (double)parameters["SlowingChirpEndValue"]);
            p.AddLinearRamp("slowingChirp", slowingNewDetuningTime, 200, (double)parameters["PokeDetuningValue"]);
            p.AddLinearRamp("slowingChirp", slowingChirpBackTime, (int)parameters["SlowingChirpDuration"], (double)parameters["SlowingChirpStartValue"]);

        }
    }
}