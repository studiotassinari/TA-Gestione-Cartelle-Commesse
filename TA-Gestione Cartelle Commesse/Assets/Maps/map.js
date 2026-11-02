// I titoli sono sempre testo; nessun nome o contenuto di relazione diventa codice HTML.
'use strict';
let map, info, markerType, rows = [], markers = [], selected = null, initialized = false;
const post = message => window.chrome.webview.postMessage(message);
window.gm_authFailure = () => post({type:'error'});
window.taMapLoaded = async () => {
  try {
    markerType = (await google.maps.importLibrary('marker')).AdvancedMarkerElement;
    const Map = (await google.maps.importLibrary('maps')).Map;
    map = new Map(document.getElementById('map'), {center:{lat:42.5,lng:12.5},zoom:6,mapId:window.taMapId,mapTypeControl:true,streetViewControl:false});
    info = new google.maps.InfoWindow(); render(); post({type:'loaded'});
  } catch { post({type:'error'}); }
};
function render(){
  if(!map)return;
  info.close(); for(const entry of markers)entry.marker.map=null; markers=[];
  const groups=new Map();
  for(const row of rows){ if(!Number.isFinite(row.lat)||!Number.isFinite(row.lng)||Math.abs(row.lat)>90||Math.abs(row.lng)>180)continue;
    const key=row.lat+','+row.lng; if(!groups.has(key))groups.set(key,[]); groups.get(key).push(row);
  }
  const bounds = new google.maps.LatLngBounds();
  for(const group of groups.values()){
    const first=group[0], position={lat:first.lat,lng:first.lng}; bounds.extend(position);
    const pin=document.createElement('div'); pin.className='pin'; pin.textContent=group.length>1?String(group.length):'●';
    const marker=new markerType({map,position,content:pin,title:group.length===1?first.title:group.length+' relazioni nella stessa posizione'});
    marker.addListener('click',()=>{
      const content=document.createElement('div');content.style.maxHeight='280px';content.style.overflowY='auto';
      for(const row of group){const button=document.createElement('button');button.type='button';button.className='report';button.textContent=row.title+' · '+row.code;button.onclick=()=>{select(row.id);post({type:'selected',id:row.id});};content.append(button);}
      info.setContent(content);info.open({map,anchor:marker});
      if(group.length===1){select(first.id);post({type:'selected',id:first.id});}
    });
    markers.push({marker,pin,group});
  }
  if(groups.size===1){map.setCenter(bounds.getCenter());map.setZoom(14);}else if(groups.size>1)map.fitBounds(bounds,45);
  select(selected,false);
}
function select(id,pan=true){
  selected=id;
  for(const entry of markers){const active=entry.group.some(r=>r.id===id);entry.pin.classList.toggle('selected',active);entry.marker.zIndex=active?1000:0;if(active&&pan)map.panTo(entry.marker.position);}
}
window.chrome.webview.addEventListener('message', event=>{
 const msg=event.data;
 if(msg.type==='init'&&!initialized){initialized=true;window.taMapId=msg.data.mapId;const script=document.createElement('script');const url=new URL('https://maps.googleapis.com/maps/api/js');url.search=new URLSearchParams({key:msg.data.apiKey,v:'weekly',loading:'async',callback:'taMapLoaded',language:'it'});script.src=url.href;script.async=true;script.onerror=()=>post({type:'error'});document.head.append(script);}
 if(msg.type==='rows'){rows=msg.data;render();}
 if(msg.type==='selection')select(msg.data);
});
post({type:'ready'});
