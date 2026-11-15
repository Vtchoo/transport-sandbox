import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';
const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text.replace(/"three(?:\/[^\"]*)?"/g, spec => JSON.stringify(import.meta.resolve(JSON.parse(spec))))).toString('base64')}`);
};
const { physicsStep, trainPerformance, curveSpeedLimit, brakingDeceleration } = await load('src/train/physics.ts');
const { TrainRoute, RouteEnd } = await load('src/train/route.ts');
const { TrackNetwork } = await load('src/track/model.ts');
const { TrainEditor } = await load('src/train/editor.ts');
const { VEHICLES } = await load('src/train/catalog.ts');
const { consistPoses } = await load('src/train/consist.ts');
const [f7, coach, dome, ge] = VEHICLES;

test('vehicle mass, locomotive power and grades affect acceleration; unpowered consists cannot drive', () => {
  const step = (defs, grade=0) => physicsStep(defs, 5, true, grade, 'realistic', 0.1).speed;
  assert.ok(step([f7]) > step([f7, coach, dome]));
  assert.ok(step([ge, coach]) > step([f7, coach]));
  assert.ok(step([f7], 0.02) < step([f7]));
  assert.ok(step([f7], -0.02) > step([f7]));
  assert.equal(physicsStep([coach], 0, true, 0, 'realistic', 1).speed, 0);
  assert.equal(trainPerformance([coach], 'realistic').maxSpeed, 0);
});
test('HO responds faster, preserves relative model top speeds, and braking reaches zero without reversing', () => {
  assert.ok(physicsStep([f7, coach], 0, true, 0, 'ho', 0.05).speed > physicsStep([f7, coach], 0, true, 0, 'realistic', 0.05).speed * 8);
  assert.ok(Math.abs(trainPerformance([f7], 'ho').maxSpeed * 3.6 - 120) < 0.001);
  assert.ok(Math.abs(trainPerformance([ge], 'ho').maxSpeed / trainPerformance([f7], 'ho').maxSpeed - ge.physics.maxSpeedMps / f7.physics.maxSpeedMps) < 1e-8);
  const stop = mode => { let v=10, distance=0, seconds=0; while(v>0 && seconds<100) { const next=physicsStep([f7, coach],v,false,0,mode,0.05);v=next.speed;distance+=next.distance;seconds+=0.05; } assert.equal(v,0);return distance; };
  assert.ok(stop('ho') < stop('realistic') / 8);
});
test('GE with passenger coaches reaches its flat-track rating and loses speed progressively uphill', () => {
  const settle = (grade, mode) => {
    let speed=0;
    for(let i=0;i<16000;i++) speed=physicsStep([ge,coach,coach],speed,true,grade,mode,0.05).speed;
    return speed;
  };
  for(const mode of ['realistic']) {
    const limit=trainPerformance([ge,coach,coach],mode).maxSpeed;
    assert.ok(Math.abs(settle(0,mode)-limit)<0.01,'Excessive resistance prevents reaching level-track rating');
    assert.ok(settle(.02,mode)>limit*.95,'Ordinary grade causes an excessive speed loss');
    const steep=settle(.08,mode);assert.ok(steep>0&&steep<limit*.8,'Grade must still reduce the power-limited equilibrium speed');
  }
});
test('HO regulated motors reach the same target uphill or downhill, with load affecting acceleration', () => {
  for (const definitions of [[ge,coach,coach],[f7,...Array(20).fill(coach)]]) {
    const limit=trainPerformance(definitions,'ho').maxSpeed;
    for (const grade of [-.4,0,.02,.08,.4]) {
      let speed=0;
      const initial=physicsStep(definitions,speed,true,grade,'ho',.05).speed;
      assert.ok(initial>0&&initial<limit,'Acceleration must be finite');
      for(let i=0;i<2000;i++)speed=physicsStep(definitions,speed,true,grade,'ho',.05).speed;
      assert.ok(Math.abs(speed-limit)<1e-8,'Grade/load must not lower HO equilibrium speed');
    }
  }
  const light=physicsStep([ge],0,true,.3,'ho',.05).speed,heavy=physicsStep([ge,coach,dome],0,true,.3,'ho',.05).speed;
  assert.ok(light>heavy);
  assert.equal(physicsStep([coach],0,true,-.4,'ho',.05).speed,0,'Unpowered cars cannot drive themselves');
});
test('HO respects curve/obstacle targets and brakes to rest with the grade-independent planning rate', () => {
  for(const grade of [-.4,0,.4]) {
    let speed=10;
    const initial=physicsStep([ge,coach],speed,true,grade,'ho',.05,3);
    assert.ok(initial.speed<speed&&initial.speed>3,'Lower targets require finite braking');
    assert.ok(Math.abs((speed-initial.speed)/.05-brakingDeceleration(grade,'ho'))<1e-8);
    for(let i=0;i<100;i++)speed=physicsStep([ge,coach],speed,true,grade,'ho',.05,3).speed;
    assert.equal(speed,3);
    for(let i=0;i<100;i++)speed=physicsStep([ge,coach],speed,false,grade,'ho',.05).speed;
    assert.equal(speed,0);
  }
});
test('curve speed follows radius and HO lateral response while realistic traction remains power limited', () => {
  assert.equal(curveSpeedLimit(Infinity,'realistic'),Infinity);
  assert.ok(Math.abs(curveSpeedLimit(400,'realistic') / curveSpeedLimit(100,'realistic') - 2) < 1e-8);
  assert.equal(curveSpeedLimit(100,'ho') / curveSpeedLimit(100,'realistic'),3);
  const low=physicsStep([f7],5,true,0,'realistic',0.01).speed-5,high=physicsStep([f7],30,true,0,'realistic',0.01).speed-30;
  assert.ok(low>high);
});
function junction() {
  const network=new TrackNetwork(),node=(x,z=0)=>network.addNode(new THREE.Vector3(x,0,z),1435);
  const a=node(-300),b=node(0),c=node(300),d=node(300,120),e=node(-300,120);
  const first=network.connect(a,b,1435),straight=network.connect(b,c,1435),branch=network.connect(b,d,1435),wrong=network.connect(b,e,1435);
  return {network,first,straight,branch,wrong};
}
test('junction routing is seeded, excludes same-side U-turns, and keeps every car on the chosen branch', () => {
  const {network,first,straight,branch,wrong}=junction(),position={edge:first.id,u:.98,facing:1};
  const choices=new Set();
  for(let seed=0;seed<20;seed++) {
    const route=new TrainRoute(network,position,seed*10000000),other=new TrainRoute(network,position,seed*10000000);
    const front=route.sample(position,50); choices.add(front.position.edge);
    assert.equal(front.position.edge,other.sample(position,50).position.edge);
    assert.notEqual(front.position.edge,wrong.id);
    const poses=consistPoses(network,[f7,coach,dome],front.position,route.sample);
    assert.ok(poses.every(p=>[first.id,front.position.edge].includes(p.position.edge)));
    const back=route.sample(front.position,-50); assert.ok(back.point.distanceTo(route.sample(position,0).point)<1e-5);
    const restored=new TrainRoute(network,front.position,0,route.snapshot());assert.deepEqual(restored.sample(front.position,-50).position,back.position);
  }
  assert.deepEqual(choices,new Set([straight.id,branch.id]));
});
test('route clearance detects endpoints and removal, and curvature measures a real-scale bend', () => {
  const n=new TrackNetwork(),a=n.addNode(new THREE.Vector3(0,0,0),1435),b=n.addNode(new THREE.Vector3(100,0,100),1435),e=n.connect(a,b,1435);
  a.axis.set(1,0,0);b.axis.set(0,0,1);e.aLength=e.bLength=55.228475;
  const p={edge:e.id,u:.5,facing:1},route=new TrainRoute(n,p,1);
  assert.ok(Math.abs(route.turnRadius(p)-100)<3);
  assert.ok(route.clearance(p,1000)>75&&route.clearance(p,1000)<85);
  assert.throws(()=>route.sample(p,1000),RouteEnd);
  n.segments.delete(e.id);assert.throws(()=>route.sample(p,0),/removed/);
});
test('loop routes distinguish repeated segment visits and can retrace the same full consist path', () => {
  const n=new TrackNetwork(),points=[[100,0],[0,100],[-100,0],[0,-100]],axes=[[0,1],[-1,0],[0,-1],[1,0]];
  const nodes=points.map(([x,z])=>n.addNode(new THREE.Vector3(x,0,z),1435));
  const edges=nodes.map((a,i)=>n.connect(a,nodes[(i+1)%4],1435));
  nodes.forEach((node,i)=>node.axis.set(axes[i][0],0,axes[i][1]));
  edges.forEach(e=>{e.aSide=1;e.bSide=-1;e.aLength=e.bLength=55.228475;});
  const p={edge:edges[0].id,u:.5,facing:1},route=new TrainRoute(n,p,42),ahead=route.sample(p,1400);
  assert.ok(ahead.position.routeIndex>=8);
  assert.ok(route.sample(ahead.position,-1400).point.distanceTo(route.sample(p,0).point)<1e-5);
  assert.equal(consistPoses(n,[f7,coach,dome],ahead.position,route.sample).length,3);
});
function fixture(run, length=1000) {
  const prior=globalThis.document;
  globalThis.document={getElementById:()=>({hidden:true,classList:{toggle(){}}}),createElement:()=>({getContext:()=>null})};
  const network=new TrackNetwork(),a=network.addNode(new THREE.Vector3(-length/2,0,0),1435),b=network.addNode(new THREE.Vector3(length/2,0,0),1435),edge=network.connect(a,b,1435);
  const editor=new TrainEditor(new THREE.Scene(),new THREE.PerspectiveCamera(),{classList:{remove(){},toggle(){}}},{origin:new THREE.Vector2()},network);
  const warnings=[];editor.onWarning=text=>warnings.push(text);
  const park=(defs,x=0)=>{const train=editor.builder.create();defs.forEach(d=>editor.builder.add(d));const position={edge:edge.id,u:(x+length/2)/length,facing:1};const visuals=editor.createVisuals(train,position);train.placed=true;editor.parked.push({train,visuals,position,supported:true});return train;};
  try {run({editor,network,edge,park,warnings});} finally {editor.dispose();if(prior===undefined)delete globalThis.document;else globalThis.document=prior;}
}
test('start/stop moves the whole train, guards editing, and brakes without picking up vehicles',()=>fixture(({editor,park,warnings})=>{
  const train=park([f7,coach]);editor.toggleRunning(train.id);assert.equal(train.running,true);
  assert.equal(editor.addVehicle('atsf-coach',train.id),false);assert.match(warnings[0],/stopped/);
  // Rejected edits restore a snapshot, so retrieve the authoritative model again.
  const model=editor.builder.trains[0];for(let i=0;i<60;i++)editor.moveTrains(1/60);
  assert.ok(model.speed>0);assert.ok(editor.parked[0].visuals[0].group.position.x>0);
  editor.toggleRunning(model.id);assert.equal(model.running,false);assert.ok(model.speed>0);
  for(let i=0;i<600;i++)editor.moveTrains(1/60);
  assert.equal(model.speed,0);assert.equal(model.placed,true);assert.equal(editor.parked[0].visuals.length,2);
}));
test('dead ends brake and reverse without flipping cars or leaving the rails',()=>fixture(({editor,park,warnings})=>{
  const train=park([f7],20);editor.physicsMode='ho';editor.toggleRunning(train.id);
  let reversed=false;
  for(let i=0;i<1800;i++){editor.moveTrains(1/60);if(train.travelDirection===-1)reversed=true;assert.ok(Math.abs(editor.parked[0].visuals[0].group.position.x)+f7.length/2<50);}
  assert.equal(reversed,true);assert.equal(train.running,true);assert.deepEqual(warnings,[]);assert.equal(train.items[0].facing,1);
},100));
test('trains stop short of occupied track, and removed supporting track stops the engine safely',()=>fixture(({editor,network,edge,park,warnings})=>{
  const train=park([f7],-100);park([f7],0);editor.physicsMode='ho';editor.toggleRunning(train.id);
  for(let i=0;i<900;i++)editor.moveTrains(1/60);
  assert.ok(editor.parked[0].visuals[0].group.position.x < -f7.length);assert.equal(train.running,true);
  network.segments.delete(edge.id);editor.moveTrains(1/60);assert.equal(train.running,false);assert.equal(train.speed,0);assert.match(warnings.at(-1),/removed/);
}));
test('a moving consist brakes before a tight bend and stays slow until its rear clears it',()=>fixture(({editor,network,edge,park,warnings})=>{
  const a=network.nodes.get(edge.a),b=network.nodes.get(edge.b);b.position.set(0,0,0);edge.aLength=edge.bLength=500/3;
  const c=network.addNode(new THREE.Vector3(100,0,100),1435),d=network.addNode(new THREE.Vector3(100,0,600),1435);
  const bend=network.connect(b,c,1435),exit=network.connect(c,d,1435);
  a.axis.set(1,0,0);b.axis.set(1,0,0);c.axis.set(0,0,1);d.axis.set(0,0,1);bend.aLength=bend.bLength=55.228475;exit.aLength=exit.bLength=500/3;
  const train=park([f7,coach],-300),p=editor.parked[0];
  train.speed=20;editor.toggleRunning(train.id);let entered=false,cleared=false;
  for(let i=0;i<4200;i++) {
    editor.moveTrains(1/30);
    if(p.position.edge===bend.id){entered=true;assert.ok(train.speed<9,'Too fast on bend');}
    if(p.position.edge===exit.id&&p.position.u<.04)assert.ok(train.speed<9,'Accelerated before rear cleared curve');
    if(p.position.edge===exit.id&&p.position.u>.12){cleared=true;break;}
  }
  assert.equal(entered,true);assert.equal(cleared,true);assert.deepEqual(warnings,[]);
}));
