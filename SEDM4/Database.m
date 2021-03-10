(* ::Package:: *)

(************************************************************************)
(* This file was generated automatically by the Mathematica front end.  *)
(* It contains Initialization cells from a Notebook file, which         *)
(* typically will have the same name as this file except ending in      *)
(* ".nb" instead of ".m".                                               *)
(*                                                                      *)
(* This file is intended to be loaded into the Mathematica kernel using *)
(* the package loading commands Get or Needs.  Doing so is equivalent   *)
(* to using the Evaluate Initialization Cells menu command in the front *)
(* end.                                                                 *)
(*                                                                      *)
(* DO NOT EDIT THIS FILE.  This entire file is regenerated              *)
(* automatically each time the parent Notebook file is saved in the     *)
(* Mathematica front end.  Any changes you make to this file will be    *)
(* overwritten.                                                         *)
(************************************************************************)



(* ::Input::Initialization:: *)
BeginPackage["SEDM4`Database`","SEDM4`EDMSuite`","NETLink`","JLink`"];


(* ::Input::Initialization:: *)
(*Adding/removing items from the database*)
addFileToDatabase::usage="addFileToDatabse[filename_] adds the given block file to the database. It uses SirCachealot to demodulate the block data before adding the demodulated block to the database.";
addFilesToDatabase::usage="addFilesToDatabase[filenames_] adds multiple block files (given as a list of filenames) to the database, with a progress bar.";
removeDBlock::usage="Removes the block with the given UID from the database.";

(*Tagging blocks*)
addTagToBlock::usage="addTagToBlock[cluster_, index_, tagToAdd_] associates a tag with a particular block (defined by its cluster and index). This association persists in the database unless explicitly removed (i.e. it doesn't go away when you re-analyse/remove dblocks etc.)";
removeTagFromBlock::usage="removeTagFromBlock[cluster_, index_, tagToRemove_] removes a tag from a block.";

(*Selection/extraction of blocks from the database*)
getDBlock::usage="getDBlock[uid_] returns a demodulated block identified by its uid from the database.";
selectByCluster::usage="selectByCluster[clusterName_] returns all demodulated blocks that are from the same cluster.";
selectByTag::usage="selectByTag[tag_] returns all demodulated blocks that have the same tag.";
uidsForTag::usage="
uidsForTag[tag_] returns all uids that have the same tag.
uidsForTag[tag_, uids_] returns all uids that have the same tag, from a list of uids.
";
uidsForAnalysisTag::usage="
uidsForAnalysisTag[tag_] returns all uids that have the same analysis tag.
uidsForAnalysisTag[tag_, uids_] returns all uids that have the same analysis tag, from a list of uids.
";
uidsForCluster::usage="
uidsForClusterTag[tag_] returns all uids for blocks from the same cluster.
uidsForClusterTag[tag_, uids_] returns all uids for blocks from the same cluster, from a list of uids.
";
uidsForMachineState::usage="
uidsForMachineState[eState_, bState_, rfState_, mwState_] returns all uids for blocks that have the same manual state.
uidsForMachineState[eState_, bState_, rfState_, mwState_, uids_] returns all uids for blocks that have the same manual state, from a list of uids.
";

(*Convenience functions to extract channels from a demodulated block*)
getPointChannelValue::usage="getPointChannelValue[channel, detector, dblock] gives the point channel value for the detector in a demodulated block dblock."
getPointChannelError::usage="getPointChannelError[channel, detector, dblock] gives the point channel error for the detector in a demodulated block dblock."
getPointChannel::usage="getPointChannel[channel, detector, dblock] gives the point channel for the detector in a demodulated block dblock in the form {value, error}."
getTOFChannelTimes::usage="getTOFChannelTimes[channel, detector, dblock] gives the TOF channel times for the detector in a demodulated block dblock."
getTOFChannelValues::usage="getTOFChannelValues[channel, detector, dblock] gives the TOF channel values for the detector in a demodulated block dblock."
getTOFChannelErrors::usage="getTOFChannelErrors[channel, detector, dblock] gives the TOF channel errors for the detector in a demodulated block dblock."
getTOFChannel::usage="getTOFChannel[channel, detector, dblock] gives the TOF channel for the detector in a demodulated block dblock in the form {{\!\(\*SubscriptBox[\(time\), \(i\)]\), \!\(\*SubscriptBox[\(value\), \(i\)]\), \!\(\*SubscriptBox[\(error\), \(i\)]\)}...}."

(*Getting the list of switches from a block*)
getSwitches::usage=
"getSwitches[dblock] returns the list of switches used in the block."

(*Finding blocks from hard disk*)
getClusterFiles::usage="getClusterFiles[clusterName_] returns a list of files that belong to the named cluster. It expects the files to be stored in the standard structure. It doesn't query the database - it really looks for files on your hard disk.";
getBlockFile::usage="getBlockFile[clusterName_, clusterIndex_] returns the filename for the block identified by its cluster name and index. It will only return a value if you have that block in your data root.";

(*Other useful functions*)
sortUids::usage="sortUids[uids_] sorts the given uid list by the blocks' timestamp."
machineStateForCluster::usage="machineStateForCluster[clusterName_] returns the machine state for a particular cluster in the form {eState, bState, rfState, mwState}.";
timeStampToDateList::usage="timeStampToDateList[ts_] converts a block timestamp (in Ticks) into a Mathematica DateList.";


(* ::Input::Initialization:: *)
analysisProgress=0;


(* ::Input::Initialization:: *)
Begin["`Private`"];


(* ::Input::Initialization:: *)
kDataVersionString="v3";


(* ::Input::Initialization:: *)
sedm4::noBlockFile="There is no file corresponding to that block on disk.";


(* ::Input::Initialization:: *)
addFileToDatabase[file_]:=$sirCachealot@AddBlock[file]


(* ::Input::Initialization:: *)
addFilesToDatabase[files_]:=Module[{},
Do[
CheckAbort[
addFileToDatabase[files[[i]]],
Print["Failed to add file: "<>files[[i]]]
];

(* Update the progress dialog *)
SEDM4`Database`analysisProgress = i/Length[files];
,
{i,Length[files]}
]
]


(* ::Input::Initialization:: *)
removeDBlock[uidToRemove_]:=$sirCachealot@DBlockStore@RemoveDBlock[uidToRemove]


(* ::Input::Initialization:: *)
addTagToBlock[cluster_,index_,tagToAdd_]:=$sirCachealot@DBlockStore@AddTagToBlock[cluster,index,tagToAdd]


(* ::Input::Initialization:: *)
removeTagFromBlock[cluster_,index_,tagToRemove_]:=$sirCachealot@DBlockStore@RemoveTagFromBlock[cluster,index,tagToRemove]


(* ::Input::Initialization:: *)
getDBlock[uid_]:=$sirCachealot@DBlockStore@GetDBlock[uid]


(* ::Input::Initialization:: *)
selectByCluster[clusterName_]:=Module[{dbs},
dbs=$sirCachealot@DBlockStore@GetDBlock[#]&/@$sirCachealot@DBlockStore@GetUIDsByCluster[clusterName];
Sort[dbs,(#1@TimeStamp@Ticks) < (#2@TimeStamp@Ticks)&]
]


(* ::Input::Initialization:: *)
selectByTag[tag_]:=$sirCachealot@DBlockStore@GetDBlock[#]&/@$sirCachealot@DBlockStore@GetUIDsByTag[tag]


(* ::Input::Initialization:: *)
uidsForTag[tag_]:=$sirCachealot@DBlockStore@GetUIDsByTag[tag]
uidsForTag[tag_,uidsIn_]:=$sirCachealot@DBlockStore@GetUIDsByTag[tag,uidsIn]
uidsForAnalysisTag[tag_]:=$sirCachealot@DBlockStore@GetUIDsByAnalysisTag[tag]
uidsForAnalysisTag[tag_,uidsIn_]:=$sirCachealot@DBlockStore@GetUIDsByAnalysisTag[tag,uidsIn]
uidsForCluster[cluster_]:=$sirCachealot@DBlockStore@GetUIDsByCluster[cluster]
uidsForCluster[cluster_,uidsIn_]:=$sirCachealot@DBlockStore@GetUIDsByCluster[cluster,uidsIn]
uidsForMachineState[eState_,bState_, rfState_,mwState_]:=$sirCachealot@DBlockStore@GetUIDsByMachineState[eState,bState,rfState,mwState];
uidsForMachineState[eState_,bState_,rfState_,mwState_,uidsIn_]:=$sirCachealot@DBlockStore@GetUIDsByMachineState[eState,bState,rfState,mwState,uidsIn]


(* ::Input::Initialization:: *)
getPointChannelValue[channel_,detector_,dblock_]:=dblock@GetPointChannel[channel,detector]@Value
getPointChannelError[channel_,detector_,dblock_]:=dblock@GetPointChannel[channel,detector]@Error
getPointChannel[channel_,detector_,dblock_]:={getPointChannelValue[channel,detector,dblock],getPointChannelError[channel,detector,dblock]}

getTOFChannelTimes[channel_,detector_,dblock_]:=dblock@GetTOFChannel[channel,detector]@Times
getTOFChannelValues[channel_,detector_,dblock_]:=dblock@GetTOFChannel[channel,detector]@Data
getTOFChannelErrors[channel_,detector_,dblock_]:=dblock@GetTOFChannel[channel,detector]@Errors
getTOFChannel[channel_,detector_,dblock_]:=Transpose[{getTOFChannelTimes[channel,detector,dblock],getTOFChannelValues[channel,detector,dblock],getTOFChannelErrors[channel,detector,dblock]}]


getSwitches[dblock_]:=Join[dblock@Config@AnalogModulations[#]@Name&/@Range[0,dblock@Config@AnalogModulations@Count-1],dblock@Config@DigitalModulations[#]@Name&/@Range[0,dblock@Config@DigitalModulations@Count-1]]


(* ::Input::Initialization:: *)
getClusterFiles[clusterName_]:=FileNames[getDirectoryFromBrokenName[breakUpClusterName[#]]<>"\\"<>#<>"*.zip"]&[clusterName]


(* ::Input::Initialization:: *)
getBlockFile[clusterName_,clusterIndex_]:=Module[{files},
files=FileNames[getDirectoryFromBrokenName[breakUpClusterName[#]]<>"\\"<>#<>"_"<>ToString[clusterIndex]<>".zip"]&[clusterName];
If[Length[files] != 1
,
Message[sedm4::noBlockFile];
Abort[];
,
files[[1]]
]
]


(* ::Input::Initialization:: *)
breakUpClusterName[cluster_]:=StringCases[cluster,RegularExpression["(\\d\\d)(\\w+)(\\d\\d)(\\d\\d)"]:>{"$1","$2","$3","$4"}][[1]];

monthReps={
"Jan"->"January",
"Feb"->"February",
"Mar"->"March",
"Apr"->"April",
"May"->"May",
"Jun"->"June",
"Jul"->"July",
"Aug"->"August",
"Sep"->"September",
"Oct"->"October",
"Nov"->"November",
"Dec"->"December"
};

getDirectoryFromBrokenName[brokenName_]:=Module[{yearString},
yearString="20" <>brokenName[[3]];
Global`$dataRoot<>"\\sedm\\"<>kDataVersionString<>"\\" <>yearString<>"\\"<>(brokenName[[2]]/.monthReps)<>yearString
];


(* ::Input::Initialization:: *)
sortUids[uids_]:=NETBlock[
SortBy[uids,timeStampToDateList[getDBlock[#]@TimeStamp@Ticks]&]
]


(* ::Input::Initialization:: *)
machineStateForCluster[clusterName_]:=NETBlock[
Module[{uid,dblock},
uid=uidsForCluster[clusterName][[1]];
dblock=getDBlock[uid];
{
dblock@Config@Settings["eState"],
dblock@Config@Settings["bState"],
dblock@Config@Settings["rfState"],
dblock@Config@Settings["mwState"]
}
]]


(* ::Input::Initialization:: *)
timeStampToDateList[ts_]:=DateList[ts/10^7+AbsoluteTime[{1,1,1,0,0,0}]//N]


(* ::Input::Initialization:: *)
End[];
EndPackage[];
