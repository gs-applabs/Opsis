"use strict";
// Time-based hysteresis; no GPU timer claims. A profile is always a ceiling.
(function(root){
 function createAdaptive(){
  const scales=[1,.8,.6,.45];let tier=0,slow=0,fast=0;
  return {
   reset(){tier=slow=fast=0;},
   sample(cost,gap,budget){
    if(![cost,gap,budget].every(Number.isFinite)||budget<=0||cost<0||gap<0)return false;
    if(cost>budget*.72||gap>budget*1.45){slow++;fast=0;}
    else if(cost<budget*.38&&gap<budget*1.16){fast++;slow=0;}
    else {slow=Math.max(0,slow-2);fast=0;}
    if(slow>=24&&tier<3){tier++;slow=fast=0;return true;}
    if(fast>=150&&tier>0){tier--;slow=fast=0;return true;}
    return false;
   },
   state(){return {tier,scale:scales[tier],slow,fast};}
  };
 }
 if(typeof module==='object'&&module.exports)module.exports=createAdaptive;
 else root.OpsisAdaptiveQuality=createAdaptive;
})(typeof window!=='undefined'?window:globalThis);
