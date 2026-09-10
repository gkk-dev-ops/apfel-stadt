import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { gzipSync } from 'node:zlib';
import { readFileSync } from 'node:fs';
import { SyncService,ApiError,sha,validateSnapshot } from '../dist/core.js';
import { MemoryDocuments,MemoryBlobs } from '../dist/memory.js';
import { makeServer } from '../dist/http.js';
const uid='agata';
const world=id=>({schemaVersion:1,simulationVersion:1,contentVersion:1,worldId:id,name:'Miasto Agaty',width:32,height:32,
 money:4000,tick:0,speed:1,seed:42,paused:true,sandbox:false,buildings:[],families:[],claimedGoals:[]});
const pack=w=>gzipSync(JSON.stringify(w));
async function setup(docs=new MemoryDocuments(),blobs=new MemoryBlobs()){
 const api=new SyncService(docs,blobs),id=randomUUID();await api.create(uid,id,'Miasto Agaty');return {api,id,docs,blobs};
}
async function push(api,id,w=world(id),base=null,key=randomUUID()) {const b=pack(w);return api.commit(uid,id,key,base,b,sha(b));}
test('parallel saves preserve both branches and advance head once',async()=>{
 const {api,id}=await setup();const [a,b]=await Promise.all([push(api,id),push(api,id,{...world(id),money:3900})]);
 assert.deepEqual([a.status,b.status].sort(),['committed','conflict']);assert.equal((await api.world(uid,id)).headSequence,1);
 const history=await api.history(uid,id,'',20);assert.equal(history.revisions.length,2);
 for(const r of history.revisions)assert.ok((await api.snapshot(uid,id,r.revisionId)).bytes.length>0);
});
test('same key concurrent replay is one revision; changed bytes rejected',async()=>{
 const {api,id,blobs}=await setup(),key=randomUUID(),bytes=pack(world(id));
 const a=await Promise.all([api.commit(uid,id,key,null,bytes,sha(bytes)),api.commit(uid,id,key,null,bytes,sha(bytes))]);
 assert.deepEqual(a[0],a[1]);assert.equal(blobs.data.size,1);
 await assert.rejects(()=>push(api,id,{...world(id),money:3999},null,key),e=>e.code==='idempotency_key_reused');
});
test('storage failure leaves recoverable pending request',async()=>{
 const {api,id,blobs}=await setup(),key=randomUUID(),bytes=pack(world(id));blobs.failNext=true;
 await assert.rejects(()=>api.commit(uid,id,key,null,bytes,sha(bytes)));
 assert.equal((await api.receipt(uid,id,key)).status,'pending');assert.equal((await api.world(uid,id)).headRevisionId,null);
 assert.equal((await api.commit(uid,id,key,null,bytes,sha(bytes))).status,'committed');
});
test('crash after blob before transaction is recoverable',async()=>{
 class Crash extends MemoryDocuments{fail=false;async transaction(fn){if(this.fail){this.fail=false;throw new Error('crash');}return super.transaction(fn);}}
 const docs=new Crash(),blobs=new MemoryBlobs(),{api,id}=await setup(docs,blobs);
 const put=blobs.put.bind(blobs);blobs.put=async(...args)=>{const r=await put(...args);docs.fail=true;blobs.put=put;return r;};
 const key=randomUUID(),b=pack(world(id));await assert.rejects(()=>api.commit(uid,id,key,null,b,sha(b)));
 assert.equal(blobs.data.size,1);assert.equal((await api.world(uid,id)).headRevisionId,null);
 assert.equal((await api.commit(uid,id,key,null,b,sha(b))).status,'committed');assert.equal(blobs.data.size,1);
});
test('delete racing upload cannot resurrect world',async()=>{
 const {api,id,blobs}=await setup(),put=blobs.put.bind(blobs);
 blobs.put=async(...args)=>{const r=await put(...args);await api.remove(uid,id,null);return r;};
 await assert.rejects(()=>push(api,id),e=>e.status===410);
 assert.ok((await api.world(uid,id,true)).deletedAt);assert.equal((await api.world(uid,id,true)).headRevisionId,null);
});
test('completed receipt replays after deletion without resurrection',async()=>{
 const {api,id}=await setup(),key=randomUUID(),a=await push(api,id,world(id),null,key);
 await api.remove(uid,id,a.revisionId);assert.deepEqual(await push(api,id,world(id),null,key),a);assert.ok((await api.world(uid,id,true)).deletedAt);
});
test('restore uses CAS and preserves newer branch',async()=>{
 const {api,id}=await setup(),a=await push(api,id),b=await push(api,id,{...world(id),money:3500},a.revisionId);
 assert.equal((await api.restore(uid,id,a.revisionId,a.revisionId,randomUUID())).status,'conflict');
 assert.equal((await api.world(uid,id)).headRevisionId,b.revisionId);
 assert.equal((await api.restore(uid,id,a.revisionId,b.revisionId,randomUUID())).status,'committed');assert.equal((await api.world(uid,id)).headSequence,3);
});
test('checksum, unsupported schema, overlap, orphan and gzip bomb fail before commit',async()=>{
 const {api,id}=await setup();const bytes=pack(world(id));
 await assert.rejects(()=>api.commit(uid,id,randomUUID(),null,bytes,'0'.repeat(64)),e=>e.code==='checksum_mismatch');
 for(const w of [{...world(id),schemaVersion:2},{...world(id),money:-1},
  {...world(id),buildings:[{id:randomUUID(),kind:1,x:1,z:1,rotation:0},{id:randomUUID(),kind:1,x:1,z:1,rotation:0}]},
  {...world(id),families:[{id:randomUUID(),homeId:randomUUID(),name:'A',mood:50}]}])await assert.rejects(()=>push(api,id,w),e=>e.status===422);
 const bomb=gzipSync(Buffer.alloc(17*1024*1024,65));await assert.rejects(()=>api.commit(uid,id,randomUUID(),null,bomb,sha(bomb)),e=>e.status===422);
 assert.equal((await api.world(uid,id)).headSequence,0);
});
test('durable quota counts attempts but excludes completed retries',async()=>{
 const {api,id,docs}=await setup(),key=randomUUID(),first=await push(api,id,world(id),null,key);
 const counters=await docs.get(api.user(uid));docs.data.set(api.user(uid),{...counters,uploads:120});
 assert.deepEqual(await push(api,id,world(id),null,key),first);
 await assert.rejects(()=>push(api,id,world(id),first.revisionId),e=>e.code==='daily_upload_limit');
});
test('world limit, idempotent creation, pagination, ownership',async()=>{
 const {api,id}=await setup();assert.equal((await api.create(uid,id,'Other')).name,'Miasto Agaty');
 for(let i=0;i<9;i++)await api.create(uid,randomUUID(),'Kolejny');
 await assert.rejects(()=>api.create(uid,randomUUID(),'Over'),e=>e.code==='world_limit');
 const a=await api.list(uid,'',4),b=await api.list(uid,a.nextCursor,4);assert.equal(new Set([...a.worlds,...b.worlds].map(w=>w.worldId)).size,8);
 await assert.rejects(()=>api.world('other',id),e=>e.status===404);assert.equal((await api.list('other','',20)).worlds.length,0);
 await api.remove(uid,id,null);await api.create(uid,randomUUID(),'Nowy');
});
test('HTTP auth, upload, conflict, checksum download, ETag',async()=>{
 const {api,id}=await setup();const server=makeServer(api,async t=>{if(t==='test-a')return uid;if(t==='test-b')return 'other';throw new ApiError(401,'invalid_or_revoked_token');},()=>{});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
 try{
  assert.equal((await fetch(base+'/healthz')).status,200);assert.equal((await fetch(base+'/v1/worlds')).status,401);
  assert.equal((await fetch(base+'/v1/worlds/'+id,{headers:{Authorization:'Bearer revoked'}})).status,401);
  assert.equal((await fetch(base+'/v1/worlds/'+id,{headers:{Authorization:'Bearer test-b'}})).status,404);
  const bytes=pack(world(id)),headers={Authorization:'Bearer test-a','Content-Type':'application/gzip','Idempotency-Key':randomUUID(),'X-Base-Revision':'none','X-Snapshot-SHA256':sha(bytes)};
  const r=await fetch(base+`/v1/worlds/${id}/revisions`,{method:'POST',headers,body:bytes});assert.equal(r.status,201);const saved=await r.json();
  const conflict=await fetch(base+`/v1/worlds/${id}/revisions`,{method:'POST',headers:{...headers,'Idempotency-Key':randomUUID()},body:bytes});assert.equal(conflict.status,409);assert.equal((await conflict.json()).status,'conflict');
  const down=await fetch(base+`/v1/worlds/${id}/revisions/${saved.revisionId}/snapshot`,{headers:{Authorization:'Bearer test-a'}});
  assert.equal(down.headers.get('x-snapshot-sha256'),sha(bytes));assert.deepEqual(Buffer.from(await down.arrayBuffer()),bytes);
  const meta=await fetch(base+'/v1/worlds/'+id,{headers:{Authorization:'Bearer test-a'}});
  assert.equal((await fetch(base+'/v1/worlds/'+id,{headers:{Authorization:'Bearer test-a','If-None-Match':meta.headers.get('etag')}})).status,304);
 }finally{await new Promise(r=>server.close(r));}
});
test('actual C# serialization accepted by TypeScript contract',async()=>{
 const w=JSON.parse(readFileSync(new URL('../../../tests/fixtures/csharp-world.json',import.meta.url),'utf8')),b=pack(w);
 assert.equal((await validateSnapshot(b,w.worldId,sha(b))).worldId,w.worldId);
});
