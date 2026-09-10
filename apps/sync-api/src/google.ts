import { initializeApp, applicationDefault } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { getFirestore, FieldPath, type DocumentData } from 'firebase-admin/firestore';
import { getStorage } from 'firebase-admin/storage';
import { ApiError, MAX_COMPRESSED, sha, type Documents, type Transaction, type Blobs } from './core.js';
export function googleAdapters(projectId:string,bucketName:string,allowed:Set<string>){
  // Production must never trust emulator tokens or silently route data to an emulator.
  for(const key of ['FIREBASE_AUTH_EMULATOR_HOST','FIRESTORE_EMULATOR_HOST','STORAGE_EMULATOR_HOST'])
    if(process.env[key])throw new Error(`${key} is forbidden in the production entry point`);
  const app=initializeApp({credential:applicationDefault(),projectId,storageBucket:bucketName});
  const db=getFirestore(app),bucket=getStorage(app).bucket();
  const docs:Documents={
    get:async<T>(p:string)=>{const d=await db.doc(p).get();return d.exists?d.data() as T:undefined;},
    list:async<T>(path:string,after:string,limit:number)=>{
      let query=db.collection(path).orderBy(FieldPath.documentId()).limit(limit);
      if(after)query=query.startAfter(after);
      const result=await query.get();return result.docs.map(d=>({id:d.id,value:d.data() as T}));
    },
    transaction:async<T>(work:(tx:Transaction)=>Promise<T>)=>db.runTransaction(async transaction=>work({
      get:async<U>(path:string)=>{const d=await transaction.get(db.doc(path));return d.exists?d.data() as U:undefined;},
      set:<U>(path:string,value:U)=>{transaction.set(db.doc(path),value as DocumentData);}
    }))
  };
  const blobs:Blobs={
    put:async(path,bytes)=>{
      const file=bucket.file(path),hash=sha(bytes);
      try{await file.save(bytes,{resumable:false,validation:'crc32c',preconditionOpts:{ifGenerationMatch:0},
        metadata:{contentType:'application/gzip',metadata:{sha256:hash}}});}
      catch(e){if((e as {code?:number}).code!==412)throw e;}
      const [metadata]=await file.getMetadata();
      if(Number(metadata.size)>MAX_COMPRESSED||metadata.metadata?.sha256!==hash)throw new ApiError(503,'immutable_blob_mismatch');
      const gen=String(metadata.generation);
      // Validate bytes even on a retry where an earlier request created the object.
      const [stored]=await bucket.file(path,{generation:gen}).download({validation:'crc32c'});
      if(sha(stored)!==hash)throw new ApiError(503,'immutable_blob_mismatch');return gen;
    },
    get:async(path,generation)=>{
      const file=bucket.file(path,{generation});const [meta]=await file.getMetadata();
      if(Number(meta.size)>MAX_COMPRESSED)throw new ApiError(503,'stored_snapshot_too_large');
      const [bytes]=await file.download({validation:'crc32c'});return bytes;
    }
  };
  const authenticate=async(token:string)=>{
    let uid:string;
    try{uid=(await getAuth(app).verifyIdToken(token,true)).uid;}catch(e){const code=(e as {code?:string}).code||'';
      if(['auth/id-token-expired','auth/id-token-revoked','auth/argument-error','auth/invalid-id-token','auth/user-disabled','auth/user-not-found'].includes(code))throw new ApiError(401,'invalid_or_revoked_token');
      throw new ApiError(503,'identity_provider_unavailable');}
    if(!allowed.has(uid))throw new ApiError(403,'tester_not_allowed');return uid;
  };
  return {docs,blobs,authenticate};
}
