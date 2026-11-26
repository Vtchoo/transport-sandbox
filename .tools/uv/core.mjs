// Engine-independent topology, seam cuts, conformal parameterization and packing.
// Coordinates are model/world units; UVs use a bottom-left origin.
export class UnionFind {
  constructor(n) { this.p = Int32Array.from({ length:n }, (_, i) => i); this.rank = new Uint8Array(n); }
  find(i) { let r=i; while(this.p[r]!==r) r=this.p[r]; while(i!==r){const n=this.p[i];this.p[i]=r;i=n;}return r; }
  join(a,b) { a=this.find(a);b=this.find(b);if(a===b)return;if(this.rank[a]<this.rank[b])[a,b]=[b,a];this.p[b]=a;if(this.rank[a]===this.rank[b])this.rank[a]++; }
}
const sub=(a,b)=>a.map((v,i)=>v-b[i]);
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0);
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const norm=a=>Math.sqrt(dot(a,a));
const unit=a=>{const l=norm(a);return a.map(v=>v/(l||1));};
export const edgeKey=(a,b)=>a<b?`${a}:${b}`:`${b}:${a}`;

export function buildTopology(faces, tolerance=0, acrossMeshes=false) {
  const vertices=[], buckets=new Map(), indices=[], edges=new Map();
  let merged=0;
  function vertex(p,mesh) {
    const scope=acrossMeshes?'':`${mesh}|`;
    const cell=tolerance>0?p.map(v=>Math.floor(v/tolerance)):p;
    if(tolerance>0){
      for(let x=-1;x<=1;x++)for(let y=-1;y<=1;y++)for(let z=-1;z<=1;z++){
        const key=scope+[cell[0]+x,cell[1]+y,cell[2]+z].join(',');
        for(const id of buckets.get(key)||[]) if(norm(sub(vertices[id],p))<=tolerance){merged++;return id;}
      }
    }else{
      const found=buckets.get(scope+cell.join(','));if(found){merged++;return found[0];}
    }
    const id=vertices.length;vertices.push(p.slice());const key=scope+cell.join(',');
    if(!buckets.has(key))buckets.set(key,[]);buckets.get(key).push(id);return id;
  }
  for(let f=0;f<faces.length;f++){
    const ids=faces[f].p.map(p=>vertex(p,faces[f].mesh));indices.push(ids);
    for(let c=0;c<3;c++){
      const a=ids[c],b=ids[(c+1)%3];if(a===b)continue;
      const key=edgeKey(a,b);if(!edges.has(key))edges.set(key,{key,a:Math.min(a,b),b:Math.max(a,b),uses:[]});
      edges.get(key).uses.push({face:f,corner:c});
    }
  }
  return {vertices,indices,edges,merged};
}
export function normal(face) {return unit(cross(sub(face.p[1],face.p[0]),sub(face.p[2],face.p[0])));}
export function sharpSeams(faces,topology,degrees,materials) {
  const result=new Set(),limit=Math.cos(degrees*Math.PI/180),normals=faces.map(normal);
  for(const e of topology.edges.values()){
    if(e.uses.length!==2){result.add(e.key);continue;}
    const [a,b]=e.uses.map(u=>u.face);
    if(dot(normals[a],normals[b])<limit||(materials&&faces[a].material!==faces[b].material))result.add(e.key);
  }
  return result;
}
export function connectedFaces(start,topology,seams) {
  const adjacency=new Map();for(const e of topology.edges.values())if(e.uses.length===2&&!seams.has(e.key)){
    const [a,b]=e.uses.map(u=>u.face);if(!adjacency.has(a))adjacency.set(a,[]);if(!adjacency.has(b))adjacency.set(b,[]);adjacency.get(a).push(b);adjacency.get(b).push(a);
  }
  const seen=new Set([start]),queue=[start];for(let i=0;i<queue.length;i++)for(const n of adjacency.get(queue[i])||[])if(!seen.has(n)){seen.add(n);queue.push(n);}return seen;
}

// Solve normal equations without assembling a dense matrix. Two pinned vertices
// remove the four null modes; each triangle supplies the Cauchy–Riemann equations.
function solveConformal(points,triangles) {
  if(points.length<3)throw new Error('Island has fewer than three vertices.');
  let a=0,b=0;
  const farthest=i=>{let best=0,d=-1;for(let k=0;k<points.length;k++){const v=dot(sub(points[k],points[i]),sub(points[k],points[i]));if(v>d){d=v;best=k;}}return best;};
  a=farthest(0);b=farthest(a);const distance=norm(sub(points[a],points[b]));
  if(distance<1e-12)throw new Error('Island is degenerate.');
  const fixed=new Map([[a*2,0],[a*2+1,0],[b*2,distance],[b*2+1,0]]),lookup=new Int32Array(points.length*2).fill(-1);
  let count=0;for(let i=0;i<lookup.length;i++)if(!fixed.has(i))lookup[i]=count++;
  const rows=[],diag=new Float64Array(count),rhs=new Float64Array(count);
  const add=entries=>{let constant=0;const row=[];for(const [id,v]of entries){if(fixed.has(id))constant-=v*fixed.get(id);else row.push([lookup[id],v]);}for(const [id,v]of row){rhs[id]+=v*constant;diag[id]+=v*v;}rows.push(row);};
  for(const tri of triangles){
    const [p,q,r]=tri.map(i=>points[i]),u=sub(q,p),v=sub(r,p),x=norm(u);if(x<1e-12)continue;
    const y=dot(u,v)/x,z=Math.sqrt(Math.max(0,dot(v,v)-y*y));if(z<1e-12)continue;
    const scale=Math.sqrt(x*z/2),gx=[-1/x,1/x,0],gy=[(y-x)/(x*z),-y/(x*z),1/z];
    add(tri.flatMap((id,i)=>[[2*id,gx[i]*scale],[2*id+1,-gy[i]*scale]]));
    add(tri.flatMap((id,i)=>[[2*id,gy[i]*scale],[2*id+1,gx[i]*scale]]));
  }
  if(!rows.length)throw new Error('No nondegenerate triangles in island.');
  const apply=(x,out)=>{out.fill(0);for(const row of rows){let value=0;for(const [id,c]of row)value+=x[id]*c;for(const [id,c]of row)out[id]+=c*value;}for(let i=0;i<count;i++)out[i]+=1e-12*x[i];};
  const x=new Float64Array(count),r=rhs.slice(),z=new Float64Array(count),p=new Float64Array(count),ap=new Float64Array(count);
  for(let i=0;i<count;i++)p[i]=z[i]=r[i]/Math.max(diag[i],1e-12);
  let rz=dot(r,z),initial=Math.max(dot(rhs,rhs),1e-30),iterations=0,converged=false;
  for(;iterations<Math.min(6000,count*4+100);iterations++){
    if(dot(r,r)/initial<1e-16){converged=true;break;}
    apply(p,ap);const denominator=dot(p,ap);if(Math.abs(denominator)<1e-30)break;
    const alpha=rz/denominator;for(let i=0;i<count;i++){x[i]+=alpha*p[i];r[i]-=alpha*ap[i];z[i]=r[i]/Math.max(diag[i],1e-12);}
    const next=dot(r,z),beta=next/rz;for(let i=0;i<count;i++)p[i]=z[i]+beta*p[i];rz=next;
  }
  const uv=points.map((_,i)=>[0,1].map(c=>fixed.has(i*2+c)?fixed.get(i*2+c):x[lookup[i*2+c]]));
  if(uv.some(v=>v.some(n=>!Number.isFinite(n))))throw new Error('Conformal solve failed. Add seams or use projection.');
  return {uv,converged,iterations};
}
function project(points,faceNormals) {
  let n=[0,0,0];for(const v of faceNormals)n=n.map((x,i)=>x+v[i]);if(norm(n)<1e-10)n=faceNormals[0]||[0,1,0];n=unit(n);
  const x=unit(cross(Math.abs(n[1])<.9?[0,1,0]:[1,0,0],n)),y=cross(n,x);
  return points.map(p=>[dot(p,x),dot(p,y)]);
}
function orient(uv,triangles) {
  // Try principal direction and the longest boundary directions. PCA alone
  // leaves square panels rotated 45 degrees because their covariance is equal.
  let mx=0,my=0;for(const p of uv){mx+=p[0]/uv.length;my+=p[1]/uv.length;}
  let xx=0,xy=0,yy=0;for(const p of uv){const x=p[0]-mx,y=p[1]-my;xx+=x*x;xy+=x*y;yy+=y*y;}
  const boundaries=new Map();for(const tri of triangles)for(let c=0;c<3;c++){const a=tri[c],b=tri[(c+1)%3],key=edgeKey(a,b);if(!boundaries.has(key))boundaries.set(key,{a,b,count:0});boundaries.get(key).count++;}
  const angles=[.5*Math.atan2(2*xy,xx-yy),...[...boundaries.values()].filter(e=>e.count===1).sort((a,b)=>Math.hypot(...sub(uv[b.a],uv[b.b]))-Math.hypot(...sub(uv[a.a],uv[a.b]))).slice(0,24).map(e=>Math.atan2(uv[e.b][1]-uv[e.a][1],uv[e.b][0]-uv[e.a][0]))];
  let best=null;
  for(const angle of angles){const c=Math.cos(angle),s=Math.sin(angle),result=uv.map(([x,y])=>[x*c+y*s,-x*s+y*c]);
    const lo=[Infinity,Infinity],hi=[-Infinity,-Infinity];for(const p of result)for(let k=0;k<2;k++){lo[k]=Math.min(lo[k],p[k]);hi[k]=Math.max(hi[k],p[k]);}
    const width=hi[0]-lo[0],height=hi[1]-lo[1],area=width*height;
    if(!best||area<best.area-1e-10||(Math.abs(area-best.area)<1e-10&&width>best.width))best={uv:result.map(p=>p.map((x,k)=>x-lo[k])),width,height,area};
  }
  // The conformal solution uses Y-up coordinates; island points are canvas
  // Y-down. Reflect here so updateUVs' bottom-left conversion preserves the
  // source triangle winding instead of mirroring every exported chart.
  best.uv=best.uv.map(([x,y])=>[x,best.height-y]);
  return best;
}
export function unwrap(faces,topology,seams,selected,{size=4096,padding=16,method='conformal'}={},progress=()=>{}) {
  if(!selected.size)throw new Error('Select at least one face.');
  const faceSets=new UnionFind(faces.length),corners=new UnionFind(faces.length*3);
  for(const e of topology.edges.values()){
    const uses=e.uses.filter(u=>selected.has(u.face));if(seams.has(e.key)||uses.length!==2||e.uses.length!==2)continue;
    const [a,b]=uses;faceSets.join(a.face,b.face);
    for(const vertex of [e.a,e.b]){const ca=topology.indices[a.face].indexOf(vertex),cb=topology.indices[b.face].indexOf(vertex);corners.join(a.face*3+ca,b.face*3+cb);}
  }
  const groups=new Map();for(const f of selected){const id=faceSets.find(f);if(!groups.has(id))groups.set(id,[]);groups.get(id).push(f);}
  const islands=[],warnings=[];
  let index=0;
  for(const faceIds of groups.values()){
    progress(`Unwrapping island ${++index} / ${groups.size}`);
    const vertexMap=new Map(),points=[],tris=[];
    for(const f of faceIds)tris.push([0,1,2].map(c=>{const key=corners.find(f*3+c);if(!vertexMap.has(key)){vertexMap.set(key,points.length);points.push(faces[f].p[c]);}return vertexMap.get(key);}));
    let area3=0;for(const f of faceIds)area3+=norm(cross(sub(faces[f].p[1],faces[f].p[0]),sub(faces[f].p[2],faces[f].p[0])))/2;
    if(area3<1e-16){warnings.push(`Skipped degenerate face group ${faceIds[0]}.`);continue;}
    let uv,solution;
    if(method==='projection')uv=project(points,faceIds.map(f=>normal(faces[f])));
    else{solution=solveConformal(points,tris);uv=solution.uv;if(!solution.converged)warnings.push(`Island ${index}: solver reached its iteration limit. Inspect distortion.`);}
    let area2=0,positive=0,negative=0;for(const [a,b,c]of tris){const u=sub(uv[b],uv[a]),v=sub(uv[c],uv[a]),d=(u[0]*v[1]-u[1]*v[0])/2;area2+=Math.abs(d);if(d>1e-12)positive++;if(d< -1e-12)negative++;}
    if(area2<1e-14)throw new Error(`Island ${index} collapsed. Add seams, mark sharp edges or use projection.`);
    if(positive&&negative)warnings.push(`Island ${index} contains flipped triangles. Add more seams.`);
    const edges=new Map();for(const tri of tris)for(let c=0;c<3;c++){const key=edgeKey(tri[c],tri[(c+1)%3]);edges.set(key,(edges.get(key)||0)+1);}
    if(![...edges.values()].includes(1))warnings.push(`Island ${index} is closed. Cut it open with seams; its UVs may overlap.`);
    const factor=Math.sqrt(area3/area2),flat=orient(uv.map(p=>p.map(v=>v*factor)),tris);
    islands.push({id:islands.length,faceIds,tris,points:flat.uv,width:flat.width,height:flat.height,area3});
  }
  if(!islands.length)throw new Error('No usable faces to unwrap.');
  const result={size,padding,islands,warnings,uvByFace:Array(faces.length).fill(null)};
  pack(result);return result;
}
export function pack(result) {
  const {size,padding,islands}=result;
  if(!Number.isFinite(size)||size<64||!Number.isFinite(padding)||padding<1||padding>=size/2)throw new Error('Use a valid atlas size and positive padding smaller than half the atlas.');
  const sorted=islands.slice().sort((a,b)=>b.height-a.height||b.width-a.width);
  function fit(scale,write=false){let x=padding,y=padding,row=0;for(const island of sorted){const w=island.width*scale,h=island.height*scale;if(w+2*padding>size||h+2*padding>size)return false;if(x+w+padding>size){x=padding;y+=row+2*padding;row=0;}if(y+h+padding>size)return false;if(write)island.rect={x,y,w,h,scale};x+=w+2*padding;row=Math.max(row,h);}return true;}
  let lo=0,hi=size/Math.max(...islands.map(i=>Math.max(i.width,i.height)),1e-9);
  for(let i=0;i<50;i++){const mid=(lo+hi)/2;if(fit(mid))lo=mid;else hi=mid;}
  if(!fit(lo,true)||lo<=1e-10)throw new Error('Too many islands for this atlas/padding. Reduce padding or increase size.');
  result.pixelsPerUnit=lo;updateUVs(result);return result;
}
export function updateUVs(result) {
  for(const island of result.islands){const {x,y,scale}=island.rect;island.faceIds.forEach((f,j)=>{result.uvByFace[f]=island.tris[j].map(id=>[(x+island.points[id][0]*scale)/result.size,1-(y+island.points[id][1]*scale)/result.size]);});}
}
export function atlasIssues(result) {
  const issues=[];for(const a of result.islands){const r=a.rect;if(r.x<0||r.y<0||r.x+r.w>result.size+.01||r.y+r.h>result.size+.01)issues.push(`Island ${a.id+1} extends outside the atlas.`);
    for(const b of result.islands){if(b.id>=a.id)continue;const s=b.rect;if(r.x<s.x+s.w&&r.x+r.w>s.x&&r.y<s.y+s.h&&r.y+r.h>s.y)issues.push(`Island ${a.id+1} overlaps island ${b.id+1} bounding box.`);}}
  return issues;
}

// Tolerance is supplied in atlas pixels, derived from the displayed canvas size.
// Snap bounding edges/centers, plus neighboring gutters matching the packer.
export function snapIsland(result,id,proposed,tolerance) {
  const guides=[],rect={...proposed},gap=result.padding*2;
  for(const axis of ['x','y']){
    const extent=axis==='x'?'w':'h',otherAxis=axis==='x'?'y':'x',otherExtent=axis==='x'?'h':'w';
    const length=proposed[extent],candidates=[];
    const add=(offset,target,kind)=>candidates.push({delta:target-(proposed[axis]+offset),position:target,axis,kind});
    add(0,result.padding,'boundary');add(length,result.size-result.padding,'boundary');add(length/2,result.size/2,'center');
    for(const island of result.islands){if(island.id===id)continue;const r=island.rect;
      for(const offset of [0,length])for(const target of [r[axis],r[axis]+r[extent]])add(offset,target,'edge');
      add(length/2,r[axis]+r[extent]/2,'center');
      if(proposed[otherAxis]<r[otherAxis]+r[otherExtent]+gap&&proposed[otherAxis]+proposed[otherExtent]>r[otherAxis]-gap){
        add(0,r[axis]+r[extent]+gap,'gutter');add(length,r[axis]-gap,'gutter');
      }
    }
    let best=null;for(const c of candidates)if(Math.abs(c.delta)<=tolerance&&(!best||Math.abs(c.delta)<Math.abs(best.delta)))best=c;
    if(best){rect[axis]+=best.delta;guides.push({axis,position:best.position,kind:best.kind});}
  }return {rect,guides};
}

function triangleTouchesBox(triangle,box) {
  const corners=[[box.x,box.y],[box.x+box.w,box.y],[box.x+box.w,box.y+box.h],[box.x,box.y+box.h]];
  const axes=[[1,0],[0,1],...triangle.map((p,i)=>{const q=triangle[(i+1)%3];return [p[1]-q[1],q[0]-p[0]];})];
  for(const axis of axes){const a=triangle.map(p=>dot(p,axis)),b=corners.map(p=>dot(p,axis));if(Math.max(...a)<Math.min(...b)-1e-9||Math.max(...b)<Math.min(...a)-1e-9)return false;}return true;
}
export function islandTouchesBox(island,box) {
  const r=island.rect;if(r.x>box.x+box.w||r.x+r.w<box.x||r.y>box.y+box.h||r.y+r.h<box.y)return false;
  return island.tris.some(tri=>triangleTouchesBox(tri.map(id=>[r.x+island.points[id][0]*r.scale,r.y+island.points[id][1]*r.scale]),box));
}
export function islandsInBox(result,start,end) {
  const box={x:Math.min(start.x,end.x),y:Math.min(start.y,end.y),w:Math.abs(end.x-start.x),h:Math.abs(end.y-start.y)},crossing=end.x<start.x;
  const ids=result.islands.filter(island=>{
    const r=island.rect;
    return crossing?islandTouchesBox(island,box):r.x>=box.x-1e-9&&r.y>=box.y-1e-9&&r.x+r.w<=box.x+box.w+1e-9&&r.y+r.h<=box.y+box.h+1e-9;
  }).map(i=>i.id);
  return {ids,box,crossing};
}
