const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const emitted=[],markers=[];let listener;
function element(){return {children:[],style:{},className:'',textContent:'',classList:{toggle(name,value){this[name]=value}},append(child){this.children.push(child)}};}
const map={setView(){return this},fitBounds(){},panTo(p){this.center=p},closePopup(){},on(name,fn){this[name]=fn;return this}};
const tiles={addTo(){return this},on(){return this}};
const L={map:()=>map,tileLayer:(url,opts)=>{assert.equal(url,'https://tile.openstreetmap.org/{z}/{x}/{y}.png');assert.match(opts.attribution,/OpenStreetMap/);return tiles},divIcon:o=>o,marker:(position,options)=>{const marker={position,options,addTo(){markers.push(this);return this},bindPopup(c){this.popup=c;return this},on(name,fn){this[name]=fn;return this},remove(){this.removed=true},setZIndexOffset(z){this.z=z},getLatLng(){return this.position}};return marker}};
const context={Map,Number,String,document:{createElement:element},L,window:{chrome:{webview:{postMessage:m=>emitted.push(m),addEventListener:(_,fn)=>listener=fn}}}};vm.createContext(context);
vm.runInContext(fs.readFileSync('TA-Gestione Cartelle Commesse/Assets/Maps/map.js','utf8'),context);
listener({data:{type:'rows',data:[{id:'1',title:'<script>test</script>',code:'C26001',lat:44,lng:12},{id:'2',title:'Seconda',code:'C26002',lat:44,lng:12},{id:'3',title:'Terza',code:'',lat:45,lng:13},{id:'4',lat:999,lng:0}]}});
assert.equal(markers.length,2);markers[1].click();assert.equal(emitted.at(-1).id,'3');assert.equal(markers[0].popup.children.length,2);assert.match(markers[0].popup.children[0].textContent,/<script>test<\/script>/);markers[0].popup.children[1].onclick();assert.equal(emitted.at(-1).id,'2');
listener({data:{type:'selection',data:'1'}});assert.equal(markers[0].z,1000);map.contextmenu({latlng:{lat:44.1,lng:12.3}});assert.equal(emitted.at(-1).type,'position');
listener({data:{type:'rows',data:[]}});assert.ok(markers.every(m=>m.removed));console.log('PASS: mappa OpenStreetMap, simboli raggruppati, selezione bidirezionale, localizzazione esplicita e titoli come testo.');
