'use strict';
// Leaflet è distribuito localmente. Al fornitore delle mappe arrivano solo le richieste delle tessere visibili.
let rows=[],markers=[],selected=null;
const post=message=>window.chrome.webview.postMessage(message);
const map=L.map('map',{zoomControl:true,attributionControl:true}).setView([42.5,12.5],6);
const tiles=L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:19,updateWhenIdle:true,keepBuffer:1,
 attribution:'&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'}).addTo(map);
tiles.on('tileerror',()=>post({type:'tiles-error'}));tiles.on('tileload',()=>post({type:'tiles-loaded'}));
map.on('contextmenu',e=>post({type:'position',lat:e.latlng.lat,lng:e.latlng.lng}));
function render(){
 map.closePopup();for(const entry of markers)entry.marker.remove();markers=[];
 const groups=new Map();
 for(const row of rows){if(!Number.isFinite(row.lat)||!Number.isFinite(row.lng)||Math.abs(row.lat)>90||Math.abs(row.lng)>180)continue;const key=row.lat+','+row.lng;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(row);}
 const bounds=[];
 for(const group of groups.values()){
  const first=group[0],position=[first.lat,first.lng];bounds.push(position);
  const pin=document.createElement('div');pin.className='pin';pin.textContent=group.length>1?String(group.length):'●';
  const marker=L.marker(position,{icon:L.divIcon({html:pin,iconSize:[34,34],iconAnchor:[17,17]}),title:group.length===1?first.title:group.length+' relazioni'}).addTo(map);
  const content=document.createElement('div');content.style.maxHeight='280px';content.style.overflowY='auto';
  for(const row of group){const button=document.createElement('button');button.type='button';button.className='report';button.textContent=row.title+' · '+row.code;button.onclick=()=>{select(row.id,false);post({type:'selected',id:row.id});};content.append(button);}
  marker.bindPopup(content);marker.on('click',()=>{if(group.length===1){select(first.id,false);post({type:'selected',id:first.id});}});
  markers.push({marker,pin,group});
 }
 if(bounds.length===1)map.setView(bounds[0],14);else if(bounds.length>1)map.fitBounds(bounds,{padding:[45,45],maxZoom:15});
 select(selected,false);
}
function select(id,pan=true){const changed=selected!==id;selected=id;if(changed&&pan)map.closePopup();for(const entry of markers){const active=entry.group.some(r=>r.id===id);entry.pin.classList.toggle('selected',active);entry.marker.setZIndexOffset(active?1000:0);if(active&&pan)map.panTo(entry.marker.getLatLng());}}
window.chrome.webview.addEventListener('message',event=>{const msg=event.data;if(msg.type==='rows'){rows=msg.data;render();}if(msg.type==='selection')select(msg.data);});
post({type:'ready'});
