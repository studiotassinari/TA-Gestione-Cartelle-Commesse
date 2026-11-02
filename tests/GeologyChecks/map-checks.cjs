const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const emitted=[],markers=[];let listener;
function element(){return {children:[],style:{},className:'',textContent:'',classList:{toggle(){}},append(child){this.children.push(child)}};}
class Marker{constructor(options){Object.assign(this,options);markers.push(this)}addListener(name,fn){this[name]=fn}}
class Info{setContent(c){this.content=c}open(){}close(){}}
class FakeMap {fitBounds(){}panTo(p){this.center=p}setCenter(p){this.center=p}setZoom(){}}
class Bounds {extend(){}getCenter(){return {lat:0,lng:0}}}
const context={URL,URLSearchParams,Map,Number,String,document:{createElement:element,getElementById:element,head:{append(){}}},google:{maps:{importLibrary:async name=>name==='marker'?{AdvancedMarkerElement:Marker}:{Map:FakeMap},InfoWindow:Info,LatLngBounds:Bounds}}};
context.window={chrome:{webview:{postMessage:m=>emitted.push(m),addEventListener:(_,fn)=>listener=fn}}};vm.createContext(context);
vm.runInContext(fs.readFileSync('TA-Gestione Cartelle Commesse/Assets/Maps/map.js','utf8'),context);
(async()=>{await context.window.taMapLoaded();listener({data:{type:'rows',data:[{id:'1',title:'<script>test</script>',code:'C26001',lat:44,lng:12},{id:'2',title:'Seconda',code:'C26002',lat:44,lng:12},{id:'3',title:'Terza',code:'',lat:45,lng:13},{id:'4',lat:999,lng:0}]}});
assert.equal(markers.length,2);markers[1].click();assert.equal(emitted.at(-1).id,'3');markers[0].click();const info=vm.runInContext('info',context);assert.equal(info.content.children.length,2);assert.match(info.content.children[0].textContent,/<script>test<\/script>/);info.content.children[1].onclick();assert.equal(emitted.at(-1).id,'2');
listener({data:{type:'rows',data:[]}});assert.equal(markers[0].map,null);assert.equal(markers[1].map,null);console.log('PASS: simboli raggruppati, selezione, titoli trattati come testo e rimozione dei simboli filtrati.');})().catch(e=>{console.error(e);process.exitCode=1});
