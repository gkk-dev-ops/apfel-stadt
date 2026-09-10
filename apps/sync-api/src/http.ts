import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';
import { ApiError, MAX_COMPRESSED, SyncService, demand, uuid } from './core.js';
type Authenticate=(token:string)=>Promise<string>;
function one(req:IncomingMessage,name:string):string {
  const value=req.headers[name];demand(typeof value==='string',`missing_${name}`,400);return value as string;
}
function body(req:IncomingMessage,limit:number):Promise<Buffer>{
  return new Promise((resolve,reject)=>{
    const chunks:Buffer[]=[];let size=0;
    const clean=()=>{req.off('data',data);req.off('end',end);req.off('error',error);req.off('aborted',aborted);};
    const error=(e:Error)=>{clean();reject(e);};const aborted=()=>error(new ApiError(400,'request_aborted'));
    const data=(chunk:Buffer)=>{size+=chunk.length;if(size>limit){clean();req.pause();reject(new ApiError(413,'body_too_large'));}else chunks.push(chunk);};
    const end=()=>{clean();resolve(Buffer.concat(chunks));};
    req.on('data',data);req.on('end',end);req.on('error',error);req.on('aborted',aborted);
  });
}
async function jsonBody(req:IncomingMessage):Promise<Record<string,unknown>>{
  demand(one(req,'content-type').split(';')[0]==='application/json','json_required',415);
  try{const parsed:unknown=JSON.parse((await body(req,32768)).toString('utf8'));
    demand(parsed&&typeof parsed==='object'&&!Array.isArray(parsed),'invalid_json',400);return parsed as Record<string,unknown>;
  }catch(e){if(e instanceof ApiError)throw e;throw new ApiError(400,'invalid_json');}
}
function baseHeader(req:IncomingMessage):string|null{const raw=one(req,'x-base-revision');demand(raw==='none'||uuid(raw),'invalid_base_revision',400);return raw==='none'?null:raw;}
function send(res:ServerResponse,status:number,data:unknown){res.statusCode=status;res.setHeader('Content-Type','application/json; charset=utf-8');res.end(JSON.stringify(data));}
export function makeServer(service:SyncService,authenticate:Authenticate,log:(v:unknown)=>void=console.log){
  let uploads=0;
  const server=createServer({maxHeaderSize:8192},async(req,res)=>{
    const requestId=randomUUID(),started=Date.now();let errorCode='';
    res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('X-Request-ID',requestId);
    res.on('finish',()=>{log({requestId,method:req.method,status:res.statusCode,elapsedMs:Date.now()-started,errorCode});if(!req.complete)req.destroy();});
    try{
      const url=new URL(req.url||'/', 'http://internal');
      if(req.method==='GET'&&url.pathname==='/healthz'){send(res,200,{status:'ok',apiVersion:1});return;}
      if(!url.pathname.startsWith('/v1/'))throw new ApiError(404,'not_found');
      const auth=req.headers.authorization;if(typeof auth!=='string'||!auth.startsWith('Bearer ')||auth.length>6144)throw new ApiError(401,'authentication_required');
      const uid=await authenticate(auth.slice(7));
      const segments=url.pathname.split('/').filter(Boolean);demand(segments[1]==='worlds','not_found',404);
      const cursor=url.searchParams.get('cursor')||'',limit=Number(url.searchParams.get('limit')||20);
      demand(Number.isInteger(limit)&&limit>=1&&limit<=50,'invalid_limit',400);
      if(segments.length===2){
        if(req.method==='GET'){send(res,200,await service.list(uid,cursor,limit));return;}
        if(req.method==='POST'){const value=await jsonBody(req);demand(uuid(value.worldId),'invalid_world_id',400);
          send(res,201,await service.create(uid,value.worldId as string,value.name));return;}
      }
      const id=segments[2];demand(uuid(id),'invalid_world_id',400);
      if(segments.length===3){
        if(req.method==='GET'){
          const w=await service.world(uid,id!,true),etag=`"${w.headSequence}:${w.headRevisionId||'none'}"`;
          res.setHeader('ETag',etag);if(req.headers['if-none-match']===etag){res.writeHead(304);res.end();return;}
          send(res,200,w);return;
        }
        if(req.method==='DELETE'){send(res,200,await service.remove(uid,id!,baseHeader(req)));return;}
      }
      if(segments[3]==='requests'&&segments.length===5&&req.method==='GET'){
        send(res,200,await service.receipt(uid,id!,segments[4]!));return;
      }
      if(segments[3]==='revisions'){
        if(segments.length===4&&req.method==='GET'){send(res,200,await service.history(uid,id!,cursor,limit));return;}
        if(segments.length===6&&segments[5]==='snapshot'&&req.method==='GET'){
          const result=await service.snapshot(uid,id!,segments[4]!);res.setHeader('Content-Type','application/gzip');
          res.setHeader('X-Snapshot-SHA256',result.hash);res.setHeader('Content-Length',result.bytes.length);res.end(result.bytes);return;
        }
        if(segments.length===4&&req.method==='POST'){
          demand(uploads<2,'upload_busy',429);uploads++;
          try{
            demand(one(req,'content-type')==='application/gzip'&&!req.headers['content-encoding'],'gzip_content_type_required',415);
            const key=one(req,'idempotency-key'),base=baseHeader(req),hash=one(req,'x-snapshot-sha256');
            await service.world(uid,id!,true); // authorize before accepting bytes
            const result=await service.commit(uid,id!,key,base,await body(req,MAX_COMPRESSED),hash);
            send(res,result.status==='conflict'?409:201,result);return;
          }finally{uploads--;}
        }
      }
      if(['restore','resolve'].includes(segments[3]||'')&&segments.length===4&&req.method==='POST'){
        demand(uploads<2,'upload_busy',429);uploads++;
        try{const b=await jsonBody(req);demand(uuid(b.sourceRevisionId),'invalid_source_revision');
          const result=await service.restore(uid,id!,b.sourceRevisionId as string,baseHeader(req),one(req,'idempotency-key'));
          send(res,result.status==='conflict'?409:201,result);return;
        }finally{uploads--;}
      }
      throw new ApiError(404,'not_found');
    }catch(e){
      const error=e instanceof ApiError?e:new ApiError(503,'service_unavailable');errorCode=error.code;
      if(error.status===429)res.setHeader('Retry-After',error.code==='daily_upload_limit'?'3600':'5');
      if(!req.complete)res.setHeader('Connection','close');
      if(!res.headersSent)send(res,error.status,{error:error.code,...error.details});else res.destroy();
    }
  });
  server.requestTimeout=60_000;server.headersTimeout=10_000;server.keepAliveTimeout=5000;
  return server;
}
