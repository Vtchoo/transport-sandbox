/** Register the native coal-fired 4000-class asset without touching other vehicles. */
import { readFile, writeFile } from 'node:fs/promises';
import { updateArrayEntry } from './lib/update-json-entry.mjs';
const root = new URL('../', import.meta.url);
const physics = {massKg:539548.123, powerW:4623340, tractiveEffortN:602166, maxSpeedMps:35.7632};
const section=(id,length,width,height,bogieCenters,extra={})=>({id,name:id,kind:'locomotive',length,width,height,bogieCenters,
  physics:{massKg:0,powerW:0,tractiveEffortN:0,maxSpeedMps:35.7632},...extra});
const definition={id:'up-big-boy',name:'Union Pacific Big Boy · Coal',kind:'locomotive',length:40.476075,width:3.4,height:4.9784,
  bodyLength:40.476075,bogieCenters:26,runningGearWheelRadius:.8636,physics,gauges:[1435],
  models:{'1435':'res://assets/vehicles/up-big-boy-1435.tscn'},thumbnail:'res://assets/vehicles/thumbnails/up-big-boy.svg',
  sections:[{offset:7.1580375,definition:section('big-boy-engine',26.16,3.4,4.9784,14,{
    bodySupports:{front:-.5,rear:-6.05},
    articulatedParts:[{node:'motion_front_engine',pivot:[0,0,.35],axleStations:[1.35,3.2,5.05,6.9]},
      {node:'motion_pilot',station:10.2},{node:'motion_trailing',station:-10}]
  })},
    {offset:-13.1400005,definition:section('big-boy-tender',14.196074,3.3,4.05,8.8)}],
  exhausts:[{position:[0,4.94,16.0380375],kind:'steam',idleRate:1.1},{position:[0,4.94,16.7280375],kind:'steam',idleRate:1.1}]};
const path=new URL('godot/assets/vehicles/catalog.json',root),raw=await readFile(path,'utf8');
await writeFile(path,updateArrayEntry(raw,definition.id,definition,'drgw-consolidation'));
const soundPath=new URL('godot/assets/audio/trains/sounds.json',root),sounds=JSON.parse(await readFile(soundPath,'utf8'));
sounds.vehicles[definition.id]={...sounds.vehicles['drgw-consolidation'],chuffPhases:[0,.17],chuffWheelRadius:.8636};
await writeFile(soundPath,JSON.stringify(sounds,null,2)+'\n');
console.log('Registered coal-fired Big Boy, 1435 mm; SI physics and one engine+tender item.');
