/** Reuse the Material Design Icons already installed by the browser game. */
import mdi from '@mdi/js';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
const icons={track:'mdiTrainVariant',vehicles:'mdiTrainCar',trains:'mdiTrain',view:'mdiEyeOutline',settings:'mdiCogOutline',camera:'mdiCameraSwitchOutline',home:'mdiHomeOutline',copy:'mdiContentCopy',close:'mdiClose',minimize:'mdiMinus',collapse:'mdiChevronUp',expand:'mdiChevronDown',resize:'mdiResizeBottomRight',station:'mdiTrainCarPassengerDoor'};
await mkdir('godot/assets/ui/icons',{recursive:true});
for(const [name,key] of Object.entries(icons)){
 if(!mdi[key])throw Error('Missing icon '+key);
 await writeFile('godot/assets/ui/icons/'+name+'.svg',`<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path fill="white" d="${mdi[key]}"/></svg>\n`);
}
await writeFile('godot/assets/ui/icons/LICENSE',await readFile('node_modules/@mdi/js/LICENSE'));
console.log('Exported '+Object.keys(icons).length+' shared Material Design Icons.');
