import { googleAdapters } from './google.js';
import { SyncService } from './core.js';
import { makeServer } from './http.js';
const project=process.env.GOOGLE_CLOUD_PROJECT,bucket=process.env.WORLDS_BUCKET;
const parsed:unknown=JSON.parse(process.env.ALLOWED_USER_UIDS||'[]');
if(!project||!bucket||!Array.isArray(parsed)||parsed.length===0||parsed.some(x=>typeof x!=='string'||x.length<1||x.length>128))
  throw new Error('GOOGLE_CLOUD_PROJECT, WORLDS_BUCKET and explicit ALLOWED_USER_UIDS are required');
const adapters=googleAdapters(project,bucket,new Set(parsed as string[]));
const server=makeServer(new SyncService(adapters.docs,adapters.blobs),adapters.authenticate);
server.listen(Number(process.env.PORT||8080),'0.0.0.0');
process.on('SIGTERM',()=>{server.close(()=>process.exit(0));server.closeIdleConnections();setTimeout(()=>process.exit(1),9000).unref();});
