import { buildTopology, unwrap } from './core.mjs';
self.onmessage=({data})=>{
  try {
    const topology=buildTopology(data.faces,data.tolerance,data.acrossMeshes);
    const result=unwrap(data.faces,topology,new Set(data.seams),new Set(data.selected),data.options,message=>self.postMessage({progress:message}));
    self.postMessage({result});
  } catch(error) {self.postMessage({error:error.message});}
};
