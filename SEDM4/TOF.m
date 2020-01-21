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
BeginPackage["SEDM4`TOF`","SEDM4`Statistics`"];


(* ::Input::Initialization:: *)
getTrimmedMeanAndErrTOFChannel::usage="getTrimmedMeanAndErrTOFChannel[tofWithErrChannels_] takes a list of TOF channels (with errors), calculates the trimmed mean and its standard error of each point of the TOF across all the channels via bootstrapping, and returns the result as a TOF with errors.";
weightedMeanOfTOFWithError::usage="weightedMeanOfTOFWithError[tofWithError_] takes a TOF with errors and returns the weighted mean and its standard error."
meanOfTOFWithError::usage="meanOfTOFWithError[tofWithError_] takes a TOF with errors and returns the mean and its standard error."


(* ::Input::Initialization:: *)



(* ::Input::Initialization:: *)
Begin["`Private`"];


(* ::Input::Initialization:: *)



(* ::Input::Initialization:: *)



(* ::Input::Initialization:: *)
getTrimmedMeanAndErrTOFChannel[tofWithErrChannels_]:=Module[{times,tme},
times=First/@tofWithErrChannels[[1]];
tme=trimmedMeanAndBSErr/@(Transpose[#[[2]]&/@#&/@tofWithErrChannels]);
Transpose[{times,First/@tme,Last/@tme}]
]


(* ::Input::Initialization:: *)
weightedMeanOfTOFWithError[tofWithError_]:=weightedMean[{#[[2]],#[[3]]}&/@tofWithError]


(* ::Input::Initialization:: *)
meanOfTOFWithError[tofWithError_]:={Mean[#[[2]]&/@tofWithError],Sqrt[Plus@@((#[[3]]&/@tofWithError)^2)/Length[tofWithError]^2]}


(* ::Input::Initialization:: *)
End[];
EndPackage[];
