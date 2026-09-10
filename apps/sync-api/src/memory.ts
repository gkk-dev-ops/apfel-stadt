// Test adapter. It is never selected by the production entry point.
import { ApiError, sha, type Documents, type Transaction, type Blobs } from './core.js';
export class MemoryDocuments implements Documents {
  data=new Map<string,unknown>();private tail:Promise<unknown>=Promise.resolve();
  async get<T>(p:string):Promise<T|undefined>{return structuredClone(this.data.get(p)) as T|undefined;}
  async list<T>(c:string,after:string,limit:number){
    return [...this.data.entries()].filter(([p])=>p.startsWith(c+'/')&&!p.slice(c.length+1).includes('/')&&p.slice(c.length+1)>after)
      .sort(([a],[b])=>a.localeCompare(b)).slice(0,limit).map(([p,v])=>({id:p.slice(c.length+1),value:structuredClone(v) as T}));
  }
  async transaction<T>(work:(t:Transaction)=>Promise<T>):Promise<T>{
    const run=this.tail.then(async()=>{const staged=new Map<string,unknown>();let wrote=false;
      const tx:Transaction={get:async<T>(p:string)=>{if(wrote)throw new Error('Reads must precede writes');return this.get<T>(p);},
        set:<T>(p:string,v:T)=>{wrote=true;staged.set(p,structuredClone(v));}};
      const result=await work(tx);for(const [k,v]of staged)this.data.set(k,v);return result;});
    this.tail=run.catch(()=>{});return run;
  }
}
export class MemoryBlobs implements Blobs {
  data=new Map<string,Buffer>(); failNext=false;
  async put(path:string,bytes:Buffer){
    if(this.failNext){this.failNext=false;throw new Error('Injected storage failure');}
    const old=this.data.get(path);if(old&&sha(old)!==sha(bytes))throw new ApiError(409,'immutable_blob_mismatch');
    if(!old)this.data.set(path,Buffer.from(bytes));return '1';
  }
  async get(path:string,generation:string){const b=this.data.get(path);if(!b||generation!=='1')throw new ApiError(503,'blob_missing');return Buffer.from(b);}
}
