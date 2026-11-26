import {test} from 'node:test';
import assert from 'node:assert/strict';
import {buildTopology,sharpSeams,connectedFaces,unwrap,edgeKey,atlasIssues,snapIsland,islandsInBox} from './core.mjs';
const face=(p,mesh=0)=>({p,mesh,material:0});
const square=[face([[0,0,0],[2,0,0],[2,1,0]]),face([[0,0,0],[2,1,0],[0,1,0]])];

test('conformal rectangle preserves angles, shared edge and relative lengths',()=>{
  const topology=buildTopology(square),r=unwrap(square,topology,new Set(),new Set([0,1]),{size:1024,padding:8});
  assert.equal(r.islands.length,1);assert.deepEqual(r.uvByFace[0][0],r.uvByFace[1][0]);assert.deepEqual(r.uvByFace[0][2],r.uvByFace[1][1]);
  const uv=r.uvByFace[0],a=uv[0].map((v,i)=>uv[1][i]-v),b=uv[1].map((v,i)=>uv[2][i]-v);
  assert.ok(Math.abs(a[0]*b[0]+a[1]*b[1])<1e-7);assert.ok(Math.abs(Math.hypot(...a)/Math.hypot(...b)-2)<1e-6);
  assert.deepEqual(atlasIssues(r),[]);
});
test('exported UVs preserve outside-facing winding, avoiding mirrored textures',()=>{
  for(const method of ['conformal','projection']){
    const r=unwrap(square,buildTopology(square),new Set(),new Set([0,1]),{size:1024,padding:8,method});
    for(const uv of r.uvByFace){const [a,b,c]=uv,signed=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);assert.ok(signed>0,`${method} must keep the positive source-face orientation in bottom-left UV coordinates`);}
  }
});
test('marked diagonal disconnects faces and duplicates its UVs',()=>{
  const t=buildTopology(square),diagonal=[...t.edges.values()].find(e=>e.uses.length===2),seams=new Set([diagonal.key]);
  assert.equal(connectedFaces(0,t,seams).size,1);const r=unwrap(square,t,seams,new Set([0,1]),{size:1024,padding:8});assert.equal(r.islands.length,2);assert.notDeepEqual(r.uvByFace[0][0],r.uvByFace[1][0]);assert.deepEqual(atlasIssues(r),[]);
});
test('welding collapses coincident edges across components without moving geometry',()=>{
  const f=[structuredClone(square[0]),{...structuredClone(square[1]),mesh:1}];f[1].p[0][0]=1e-7;const before=JSON.stringify(f);
  assert.equal(buildTopology(f).vertices.length,6);const t=buildTopology(f,1e-5,true);assert.equal(t.vertices.length,4);assert.equal([...t.edges.values()].filter(e=>e.uses.length===2).length,1);assert.equal(JSON.stringify(f),before);
});
test('automatic seams cut a folded surface and material boundaries',()=>{
  const f=[face([[0,0,0],[1,0,0],[0,1,0]]),face([[1,0,0],[0,0,0],[0,0,1]])],t=buildTopology(f),joint=[...t.edges.values()].find(e=>e.uses.length===2);
  assert.ok(sharpSeams(f,t,55,false).has(joint.key));
  const flat=structuredClone(square);flat[1].material=1;const ft=buildTopology(flat),edge=[...ft.edges.values()].find(e=>e.uses.length===2);assert.ok(sharpSeams(flat,ft,55,true).has(edge.key));assert.ok(!sharpSeams(flat,ft,55,false).has(edge.key));
});
test('unselected faces have no new UVs and atlas bounds respect padding',()=>{
  const r=unwrap(square,buildTopology(square),new Set(),new Set([1]),{size:512,padding:16});assert.equal(r.uvByFace[0],null);
  for(const uv of r.uvByFace[1])for(const n of uv)assert.ok(n>=16/512-1e-9&&n<=1-16/512+1e-9);
});
test('closed surface cannot silently produce a clean unwrap',()=>{
  const p=[[1,1,1],[-1,-1,1],[-1,1,-1],[1,-1,-1]],f=[[0,2,1],[0,1,3],[0,3,2],[1,2,3]].map(t=>face(t.map(i=>p[i]))),top=buildTopology(f);
  assert.throws(()=>unwrap(f,top,new Set(),new Set([0,1,2,3]),{size:512,padding:8}),/seams/);
});
test('empty and degenerate selections fail with an actionable error',()=>{
  assert.throws(()=>unwrap(square,buildTopology(square),new Set(),new Set()),/Select/);
  const f=[face([[0,0,0],[0,0,0],[0,0,0]])];assert.throws(()=>unwrap(f,buildTopology(f),new Set(),new Set([0])),/No usable/);
});

const snapLayout={size:1024,padding:8,islands:[{id:0,rect:{x:100,y:100,w:100,h:80}},{id:1,rect:{x:400,y:300,w:200,h:120}}]};
test('islands snap to padded atlas borders and centers within tolerance',()=>{
  const a=snapIsland(snapLayout,0,{x:11,y:700,w:100,h:80},5);assert.equal(a.rect.x,8);assert.equal(a.rect.y,700);assert.equal(a.guides[0].kind,'boundary');
  const b=snapIsland(snapLayout,0,{x:465,y:700,w:100,h:80},5);assert.equal(b.rect.x,462);assert.ok(b.guides.some(g=>g.position===512));
});
test('islands align edges and keep a packed gutter beside a neighbor',()=>{
  const aligned=snapIsland(snapLayout,0,{x:403,y:800,w:100,h:80},5);assert.equal(aligned.rect.x,400);
  const adjacent=snapIsland(snapLayout,0,{x:619,y:310,w:100,h:80},5);assert.equal(adjacent.rect.x,616);assert.ok(adjacent.guides.some(g=>g.kind==='gutter'));
});
test('snapping leaves distant positions alone and excludes the dragged island',()=>{
  const proposed={x:245,y:710,w:100,h:80},before=JSON.stringify(snapLayout),r=snapIsland(snapLayout,0,proposed,3);assert.deepEqual(r.rect,proposed);assert.deepEqual(r.guides,[]);assert.equal(JSON.stringify(snapLayout),before);
  const self=snapIsland({size:1024,padding:8,islands:[snapLayout.islands[0]]},0,{x:102,y:103,w:100,h:80},5);assert.equal(self.rect.x,102);assert.equal(self.rect.y,103);
});

const boxLayout={islands:[{id:0,rect:{x:0,y:0,w:10,h:10,scale:1},points:[[0,0],[10,0],[0,10]],tris:[[0,1,2]]}]};
test('window selection requires full containment and depends on horizontal direction only',()=>{
  assert.deepEqual(islandsInBox(boxLayout,{x:-1,y:-1},{x:11,y:11}).ids,[0]);
  assert.deepEqual(islandsInBox(boxLayout,{x:-1,y:11},{x:11,y:-1}).ids,[0]);
  assert.deepEqual(islandsInBox(boxLayout,{x:-1,y:-1},{x:5,y:5}).ids,[]);
});
test('crossing selects touched triangles including edges and contained rectangles',()=>{
  assert.deepEqual(islandsInBox(boxLayout,{x:5,y:5},{x:-1,y:-1}).ids,[0]);
  assert.deepEqual(islandsInBox(boxLayout,{x:11,y:1},{x:10,y:-1}).ids,[0]);
  assert.deepEqual(islandsInBox(boxLayout,{x:2,y:2},{x:1,y:1}).ids,[0]);
});
test('crossing ignores empty corners of island bounds and respects packed transforms',()=>{
  assert.deepEqual(islandsInBox(boxLayout,{x:9,y:9},{x:8,y:8}).ids,[]);
  const transformed=structuredClone(boxLayout);transformed.islands[0].rect={x:100,y:200,w:20,h:20,scale:2};
  assert.deepEqual(islandsInBox(transformed,{x:105,y:205},{x:99,y:199}).ids,[0]);
  assert.deepEqual(islandsInBox(transformed,{x:5,y:5},{x:-1,y:-1}).ids,[]);
});
