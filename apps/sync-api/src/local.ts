import { SyncService } from './core.js';
import { makeServer } from './http.js';
import { MemoryBlobs, MemoryDocuments } from './memory.js';

const uid=process.env.LOCAL_USER_UID||'local-player';
const token=process.env.LOCAL_AUTH_TOKEN||'local-dev-token';
const server=makeServer(new SyncService(new MemoryDocuments(),new MemoryBlobs()),async supplied=>{
  if(supplied!==token)throw new Error('invalid local token');return uid;
});
server.listen(Number(process.env.PORT||8080),'0.0.0.0',()=>console.log(`Local sync API listening on :${process.env.PORT||8080}`));
process.on('SIGTERM',()=>server.close(()=>process.exit(0)));
