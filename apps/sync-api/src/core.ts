import { createHash, randomUUID } from 'node:crypto';
import { gunzipSync } from 'node:zlib';

export const MAX_COMPRESSED=8*1024*1024;
const MAX_UNCOMPRESSED=16*1024*1024,MAX_WORLDS=10,MAX_UPLOADS=120;
export class ApiError extends Error {
  constructor(public status:number,public code:string,public details:Record<string,unknown>={}){super(code);}
}
export function demand(value:unknown,code:string,status=400):asserts value {
  if(!value)throw new ApiError(status,code);
}
export const uuid=(value:unknown):value is string=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
export const sha=(bytes:Buffer)=>createHash('sha256').update(bytes).digest('hex');

export interface Transaction {get<T>(path:string):Promise<T|undefined>;set<T>(path:string,value:T):void;}
export interface Documents {
  get<T>(path:string):Promise<T|undefined>;
  list<T>(path:string,after:string,limit:number):Promise<Array<{id:string,value:T}>>;
  transaction<T>(work:(tx:Transaction)=>Promise<T>):Promise<T>;
}
export interface Blobs {put(path:string,bytes:Buffer):Promise<string>;get(path:string,generation:string):Promise<Buffer>;}

type WorldDoc={worldId:string;name:string;owner:string;createdAt:string;updatedAt:string;deletedAt?:string;headRevisionId:string|null;headSequence:number};
type Revision={revisionId:string;worldId:string;createdAt:string;sequence:number;baseRevisionId:string|null;status:'committed'|'conflict';blobPath:string;blobGeneration:string;sha256:string;sourceRevisionId?:string};
type CommitResult={status:'committed'|'conflict';revisionId:string;headRevisionId:string|null;headSequence:number};
type Receipt={status:'pending'|'complete';hash:string;revisionId:string;result?:CommitResult};
type UserDoc={worlds:number;uploads:number;uploadDay:string};

function record(value:unknown):value is Record<string,unknown>{return !!value&&typeof value==='object'&&!Array.isArray(value);}
function integer(value:unknown,min:number,max:number){return Number.isInteger(value)&&Number(value)>=min&&Number(value)<=max;}
function text(value:unknown,max:number){return typeof value==='string'&&value.length>0&&value.length<=max;}

export async function validateSnapshot(bytes:Buffer,worldId:string,expectedHash:string):Promise<Record<string,unknown>>{
  demand(bytes.length>0&&bytes.length<=MAX_COMPRESSED,'snapshot_too_large',422);
  demand(/^[0-9a-f]{64}$/i.test(expectedHash)&&sha(bytes)===expectedHash,'checksum_mismatch',422);
  let unpacked:Buffer;
  try{unpacked=gunzipSync(bytes,{maxOutputLength:MAX_UNCOMPRESSED});}catch{throw new ApiError(422,'invalid_snapshot');}
  let value:unknown;
  try{value=JSON.parse(unpacked.toString('utf8'));}catch{throw new ApiError(422,'invalid_snapshot');}
  demand(record(value),'invalid_snapshot',422);
  demand(value.schemaVersion===1&&value.simulationVersion===1&&value.contentVersion===1,'unsupported_snapshot_version',422);
  demand(value.worldId===worldId&&uuid(value.worldId),'invalid_world_id',422);
  demand(text(value.name,128)&&integer(value.width,1,512)&&integer(value.height,1,512),'invalid_snapshot',422);
  demand(typeof value.money==='number'&&Number.isFinite(value.money)&&value.money>=0,'invalid_money',422);
  demand(integer(value.tick,0,Number.MAX_SAFE_INTEGER)&&integer(value.speed,0,16)&&typeof value.paused==='boolean'&&typeof value.sandbox==='boolean','invalid_snapshot',422);
  demand(Array.isArray(value.buildings)&&value.buildings.length<=100000&&Array.isArray(value.families)&&value.families.length<=100000,'invalid_snapshot',422);
  const occupied=new Set<string>(),buildingIds=new Set<string>();
  for(const item of value.buildings){
    demand(record(item)&&uuid(item.id)&&integer(item.kind,0,10000)&&integer(item.x,0,Number(value.width)-1)&&integer(item.z,0,Number(value.height)-1)&&integer(item.rotation,0,3),'invalid_building',422);
    demand(!buildingIds.has(item.id)&&!occupied.has(`${item.x}:${item.z}`),'overlapping_buildings',422);
    buildingIds.add(item.id);occupied.add(`${item.x}:${item.z}`);
  }
  const familyIds=new Set<string>();
  for(const item of value.families){
    demand(record(item)&&uuid(item.id)&&uuid(item.homeId)&&buildingIds.has(item.homeId)&&text(item.name,128)&&typeof item.mood==='number'&&item.mood>=0&&item.mood<=100,'orphan_family',422);
    demand(!familyIds.has(item.id),'duplicate_family',422);familyIds.add(item.id);
  }
  demand(Array.isArray(value.claimedGoals)&&value.claimedGoals.every(v=>typeof v==='string'&&v.length<=128),'invalid_goals',422);
  return value;
}

export class SyncService {
  constructor(private docs:Documents,private blobs:Blobs){}
  user(uid:string){return `users/${uid}`;}
  private worlds(uid:string){return `${this.user(uid)}/worlds`;}
  private worldPath(uid:string,id:string){return `${this.worlds(uid)}/${id}`;}
  private revisions(uid:string,id:string){return `${this.worldPath(uid,id)}/revisions`;}
  private receiptPath(uid:string,id:string,key:string){return `${this.worldPath(uid,id)}/requests/${key}`;}
  private today(){return new Date().toISOString().slice(0,10);}

  async create(uid:string,id:string,name:unknown){
    demand(uuid(id),'invalid_world_id');demand(typeof name==='string'&&name.trim().length>0&&name.length<=128,'invalid_name');
    return this.docs.transaction(async tx=>{
      const path=this.worldPath(uid,id),existing=await tx.get<WorldDoc>(path);if(existing)return existing;
      const user=await tx.get<UserDoc>(this.user(uid))||{worlds:0,uploads:0,uploadDay:this.today()};
      demand(user.worlds<MAX_WORLDS,'world_limit',429);
      const now=new Date().toISOString(),world:WorldDoc={worldId:id,name:name.trim(),owner:uid,createdAt:now,updatedAt:now,headRevisionId:null,headSequence:0};
      tx.set(this.user(uid),{...user,worlds:user.worlds+1});tx.set(path,world);return world;
    });
  }
  async list(uid:string,after:string,limit:number){
    const rows=await this.docs.list<WorldDoc>(this.worlds(uid),after,limit+1),active=rows.filter(r=>!r.value.deletedAt),page=active.slice(0,limit);
    return {worlds:page.map(r=>r.value),nextCursor:active.length>limit?page[page.length-1]?.id||'':''};
  }
  async world(uid:string,id:string,includeDeleted=false){
    const value=await this.docs.get<WorldDoc>(this.worldPath(uid,id));
    if(!value||(!includeDeleted&&value.deletedAt))throw new ApiError(404,'world_not_found');return value;
  }
  async history(uid:string,id:string,after:string,limit:number){
    await this.world(uid,id);const rows=await this.docs.list<Revision>(this.revisions(uid,id),after,limit+1),page=rows.slice(0,limit);
    return {revisions:page.map(r=>r.value),nextCursor:rows.length>limit?page[page.length-1]?.id||'':''};
  }
  async snapshot(uid:string,id:string,revisionId:string){
    await this.world(uid,id,true);const revision=await this.docs.get<Revision>(`${this.revisions(uid,id)}/${revisionId}`);
    if(!revision)throw new ApiError(404,'revision_not_found');const bytes=await this.blobs.get(revision.blobPath,revision.blobGeneration);
    demand(sha(bytes)===revision.sha256,'stored_snapshot_mismatch',503);return {bytes,hash:revision.sha256};
  }
  async receipt(uid:string,id:string,key:string){
    const value=await this.docs.get<Receipt>(this.receiptPath(uid,id,key));if(!value)throw new ApiError(404,'request_not_found');return value.status==='complete'?value.result!:value;
  }
  async commit(uid:string,id:string,key:string,base:string|null,bytes:Buffer,hash:string):Promise<CommitResult>{
    demand(uuid(key),'invalid_idempotency_key');demand(base===null||uuid(base),'invalid_base_revision');await validateSnapshot(bytes,id,hash);
    const receiptPath=this.receiptPath(uid,id,key),revisionId=await this.docs.transaction(async tx=>{
      const old=await tx.get<Receipt>(receiptPath);
      if(old){demand(old.hash===hash,'idempotency_key_reused',409);if(old.status==='complete')return old.result!;return old.revisionId;}
      const world=await tx.get<WorldDoc>(this.worldPath(uid,id));if(!world||world.deletedAt)throw new ApiError(404,'world_not_found');
      let user=await tx.get<UserDoc>(this.user(uid))||{worlds:1,uploads:0,uploadDay:this.today()};const day=this.today();if(user.uploadDay!==day)user={...user,uploads:0,uploadDay:day};
      demand(user.uploads<MAX_UPLOADS,'daily_upload_limit',429);const next=randomUUID();tx.set(this.user(uid),{...user,uploads:user.uploads+1});tx.set(receiptPath,{status:'pending',hash,revisionId:next} satisfies Receipt);return next;
    });
    if(typeof revisionId!=='string')return revisionId;
    const blobPath=`worlds/${uid}/${id}/${revisionId}.json.gz`,generation=await this.blobs.put(blobPath,bytes);
    return this.docs.transaction(async tx=>{
      const receipt=await tx.get<Receipt>(receiptPath);if(!receipt)throw new ApiError(503,'request_lost');if(receipt.status==='complete')return receipt.result!;
      const world=await tx.get<WorldDoc>(this.worldPath(uid,id));if(!world)throw new ApiError(404,'world_not_found');if(world.deletedAt)throw new ApiError(410,'world_deleted');
      const committed=world.headRevisionId===base,sequence=world.headSequence+(committed?1:0),status=committed?'committed':'conflict';
      const revision:Revision={revisionId,worldId:id,createdAt:new Date().toISOString(),sequence,baseRevisionId:base,status,blobPath,blobGeneration:generation,sha256:hash};
      const result:CommitResult={status,revisionId,headRevisionId:committed?revisionId:world.headRevisionId,headSequence:sequence};
      tx.set(`${this.revisions(uid,id)}/${revisionId}`,revision);if(committed)tx.set(this.worldPath(uid,id),{...world,updatedAt:revision.createdAt,headRevisionId:revisionId,headSequence:sequence});
      tx.set(receiptPath,{...receipt,status:'complete',result});return result;
    });
  }
  async remove(uid:string,id:string,base:string|null){
    return this.docs.transaction(async tx=>{const path=this.worldPath(uid,id),world=await tx.get<WorldDoc>(path);if(!world||world.deletedAt)throw new ApiError(404,'world_not_found');
      demand(world.headRevisionId===base,'revision_conflict',409);const user=await tx.get<UserDoc>(this.user(uid));const deletedAt=new Date().toISOString();
      tx.set(path,{...world,deletedAt,updatedAt:deletedAt});if(user)tx.set(this.user(uid),{...user,worlds:Math.max(0,user.worlds-1)});return {deletedAt};});
  }
  async restore(uid:string,id:string,sourceRevisionId:string,base:string|null,key:string):Promise<CommitResult>{
    demand(uuid(key)&&uuid(sourceRevisionId),'invalid_request');const receiptPath=this.receiptPath(uid,id,key);
    return this.docs.transaction(async tx=>{const old=await tx.get<Receipt>(receiptPath);if(old?.status==='complete')return old.result!;
      const path=this.worldPath(uid,id),world=await tx.get<WorldDoc>(path);if(!world||world.deletedAt)throw new ApiError(404,'world_not_found');
      const source=await tx.get<Revision>(`${this.revisions(uid,id)}/${sourceRevisionId}`);if(!source)throw new ApiError(404,'revision_not_found');
      const revisionId=old?.revisionId||randomUUID(),committed=world.headRevisionId===base,sequence=world.headSequence+(committed?1:0),status=committed?'committed':'conflict',createdAt=new Date().toISOString();
      const revision:Revision={...source,revisionId,createdAt,sequence,baseRevisionId:base,status,sourceRevisionId};
      const result:CommitResult={status,revisionId,headRevisionId:committed?revisionId:world.headRevisionId,headSequence:sequence};tx.set(`${this.revisions(uid,id)}/${revisionId}`,revision);
      if(committed)tx.set(path,{...world,updatedAt:createdAt,headRevisionId:revisionId,headSequence:sequence});tx.set(receiptPath,{status:'complete',hash:source.sha256,revisionId,result});return result;});
  }
}
