using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Town.Domain;
using UnityEngine;
using UnityEngine.Networking;

namespace Town.Runtime
{
    [Serializable] public sealed class ClientConfig { public string apiBaseUrl="",authApiKey="",authProjectId=""; }
    [Serializable] sealed class LoginBody { public string email,password;public bool returnSecureToken=true; }
    [Serializable] sealed class LoginReply { public string localId,idToken,refreshToken,expiresIn; }
    [Serializable] sealed class RefreshReply { public string user_id,id_token,refresh_token,expires_in; }
    [Serializable] public sealed class RemoteWorld { public string worldId,name,headRevisionId,deletedAt,updatedAt;public long headSequence; }
    [Serializable] public sealed class WorldList { public RemoteWorld[] worlds;public string nextCursor; }
    [Serializable] public sealed class RemoteRevision { public string revisionId,parentRevisionId,createdAt,state;public long tick; }
    [Serializable] public sealed class HistoryList { public RemoteRevision[] revisions;public string nextCursor; }
    [Serializable] sealed class Receipt { public string status,revisionId,headRevisionId,sha256;public long headSequence; }
    [Serializable] sealed class CreateBody { public string worldId,name; }
    [Serializable] sealed class RestoreBody { public string sourceRevisionId; }
    public sealed class CloudException:Exception
    { public readonly long Status;public CloudException(long status,string message):base(message){Status=status;} }
    public sealed class HttpReply { public long status;public byte[] bytes;public string hash;public int retryAfter;public string Text=>Encoding.UTF8.GetString(bytes); }
    public static class TownHttp
    {
        public static async Task<HttpReply> Send(string method,string url,byte[] bytes=null,string contentType="application/json",string token=null,string key=null,string basis=null,string hash=null)
        {
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")throw new CloudException(0,"Adres API musi używać HTTPS.");
            using(var request=new UnityWebRequest(url,method))
            {
                request.downloadHandler=new DownloadHandlerBuffer();request.timeout=25;
                if(bytes!=null){request.uploadHandler=new UploadHandlerRaw(bytes);request.SetRequestHeader("Content-Type",contentType);}
                if(token!=null)request.SetRequestHeader("Authorization","Bearer "+token);
                if(key!=null)request.SetRequestHeader("Idempotency-Key",key);
                if(basis!=null)request.SetRequestHeader("X-Base-Revision",basis==""?"none":basis);
                if(hash!=null)request.SetRequestHeader("X-Snapshot-SHA256",hash);
                var operation=request.SendWebRequest();while(!operation.isDone)await Task.Yield();
                if(request.responseCode==0)throw new CloudException(0,"Brak połączenia. Zapis na urządzeniu jest zachowany.");
                int.TryParse(request.GetResponseHeader("Retry-After"),out var retry);
                return new HttpReply{status=request.responseCode,bytes=request.downloadHandler.data??Array.Empty<byte>(),hash=request.GetResponseHeader("X-Snapshot-SHA256"),retryAfter=retry};
            }
        }
        public static void RequireSuccess(HttpReply reply)
        {
            if(reply.status>=200&&reply.status<300)return;
            string message=reply.status==401?"Zaloguj się ponownie.":reply.status==403?"To konto nie ma dostępu do testów.":
                reply.status==410?"Świat zarchiwizowano w chmurze. Możesz zachować lokalną kopię.":
                reply.status==429?"Limit synchronizacji. Spróbuj później; lokalny zapis pozostaje dostępny.":
                reply.status==413?"Świat jest za duży do synchronizacji tej wersji.":
                reply.status==422?"Serwer odrzucił format zapisu. Lokalna kopia jest zachowana.":"Synchronizacja niedostępna ("+reply.status+"). Lokalny zapis jest zachowany.";
            throw new CloudException(reply.status,message);
        }
    }
    public sealed class TownSession
    {
        readonly ClientConfig config;
        readonly SemaphoreSlim refreshGate=new SemaphoreSlim(1,1);
        string access="",refresh="";DateTime expires=DateTime.MinValue;
        public string Uid{get;private set;}="";
        public int Stamp{get;private set;}
        string SecretKey=>"town.refresh."+config.authProjectId;
        public bool Configured=>!string.IsNullOrWhiteSpace(config.apiBaseUrl)&&!string.IsNullOrWhiteSpace(config.authApiKey)&&!string.IsNullOrWhiteSpace(config.authProjectId);
        public bool SignedIn=>Uid!="";
        public TownSession(ClientConfig config){this.config=config;}
        public async Task Login(string email,string password)
        {
            if(!Configured)throw new CloudException(0,"Ten build nie ma jeszcze konfiguracji GCP. Gra offline jest dostępna.");
            var stamp=++Stamp;
            var reply=await TownHttp.Send("POST","https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key="+Uri.EscapeDataString(config.authApiKey),
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(new LoginBody{email=email.Trim(),password=password})));
            if(stamp!=Stamp)throw new OperationCanceledException();
            if(reply.status!=200)throw new CloudException(reply.status,"Nie udało się zalogować. Sprawdź dane i dostęp konta.");
            var data=JsonUtility.FromJson<LoginReply>(reply.Text);
            Accept(data.localId,data.idToken,data.refreshToken,data.expiresIn);AppleServices.StoreSecret(SecretKey,refresh);
        }
        void Accept(string uid,string token,string refreshToken,string seconds)
        {
            if(string.IsNullOrEmpty(uid)||string.IsNullOrEmpty(token)||string.IsNullOrEmpty(refreshToken))throw new CloudException(0,"Nieprawidłowa odpowiedź logowania.");
            Uid=uid;access=token;refresh=refreshToken;int.TryParse(seconds,out var ttl);expires=DateTime.UtcNow.AddSeconds(Math.Max(60,ttl)-45);
        }
        public async Task RestoreSession()
        {
            if(!Configured)return;refresh=AppleServices.ReadSecret(SecretKey);if(refresh=="")return;await EnsureToken(true);
        }
        async Task EnsureToken(bool force=false)
        {
            await refreshGate.WaitAsync();
            try{
                if(!force&&DateTime.UtcNow<expires&&access!="")return;
                if(refresh=="")throw new CloudException(401,"Zaloguj się, żeby zsynchronizować światy.");
                int stamp=Stamp;
                var bytes=Encoding.UTF8.GetBytes("grant_type=refresh_token&refresh_token="+Uri.EscapeDataString(refresh));
                var reply=await TownHttp.Send("POST","https://securetoken.googleapis.com/v1/token?key="+Uri.EscapeDataString(config.authApiKey),bytes,"application/x-www-form-urlencoded");
                if(stamp!=Stamp)throw new OperationCanceledException();
                if(reply.status!=200)throw new CloudException(reply.status,"Sesja wymaga ponownego logowania. Możesz nadal grać offline.");
                var d=JsonUtility.FromJson<RefreshReply>(reply.Text);Accept(d.user_id,d.id_token,d.refresh_token,d.expires_in);AppleServices.StoreSecret(SecretKey,refresh);
            }finally{refreshGate.Release();}
        }
        public void Logout(){Stamp++;Uid="";access="";refresh="";expires=DateTime.MinValue;AppleServices.DeleteSecret(SecretKey);}
        public async Task<HttpReply> Api(string method,string path,byte[] bytes=null,string contentType="application/json",string key=null,string basis=null,string hash=null)
        {
            if(!Configured)throw new CloudException(0,"Ten build działa offline — brak konfiguracji GCP.");
            var stamp=Stamp;
            for(int attempt=0;attempt<3;attempt++)
            {
                await EnsureToken();if(stamp!=Stamp)throw new OperationCanceledException();
                HttpReply result;
                try{result=await TownHttp.Send(method,config.apiBaseUrl.TrimEnd('/')+path,bytes,contentType,access,key,basis,hash);}
                catch(CloudException e)when(e.Status==0&&attempt<2){await Task.Delay(500*(attempt+1));continue;}
                if(stamp!=Stamp)throw new OperationCanceledException();
                if(result.status==401&&attempt==0){await EnsureToken(true);continue;}
                if((result.status>=500||result.status==429)&&attempt<2&&result.retryAfter<=10)
                {await Task.Delay(Math.Max(500*(attempt+1),result.retryAfter*1000));continue;}
                return result;
            }
            throw new CloudException(0,"Synchronizacja chwilowo niedostępna.");
        }
    }
    public sealed class CloudSync
    {
        readonly TownSession session;readonly Func<LocalWorld> current;readonly Action saveNow;readonly Action<World> preserve;readonly Func<string,LocalWorld> findLocal;
        public bool Busy{get;private set;}
        public string Status{get;private set;}="Na urządzeniu";
        public CloudSync(TownSession session,Func<LocalWorld> current,Action saveNow,Action<World> preserve,Func<string,LocalWorld> findLocal)
        {this.findLocal=findLocal;this.session=session;this.current=current;this.saveNow=saveNow;this.preserve=preserve;}
        string Path(LocalWorld local)=>"/v1/worlds/"+local.world.worldId;
        void Guard(LocalWorld local,int stamp)
        {if(current()!=local||session.Stamp!=stamp)throw new OperationCanceledException();}
        public async Task<WorldList> List(string cursor="")
        {var r=await session.Api("GET","/v1/worlds?limit=20&cursor="+Uri.EscapeDataString(cursor));TownHttp.RequireSuccess(r);return JsonUtility.FromJson<WorldList>(r.Text);}
        public async Task<HistoryList> History(string cursor="")
        {var r=await session.Api("GET",Path(current())+"/revisions?limit=20&cursor="+Uri.EscapeDataString(cursor));TownHttp.RequireSuccess(r);return JsonUtility.FromJson<HistoryList>(r.Text);}
        public async Task Push()
        {
            if(Busy)return;Busy=true;Status="Synchronizuję…";
            try{await PushCore();}catch{Status="Na urządzeniu — spróbuj ponownie";throw;}finally{Busy=false;}
        }
        async Task PushCore()
        {
            var local=current();int stamp=session.Stamp;
            if(local.ownerUid!=""&&local.ownerUid!=session.Uid)throw new CloudException(403,"Ten świat należy do innego konta. Utwórz kopię, aby przypisać ją do bieżącego konta.");
            if(local.conflictRevisionId!=""){Status="Dwie wersje świata — wybierz poniżej";return;}
            var created=await session.Api("POST","/v1/worlds",Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CreateBody{worldId=local.world.worldId,name=local.world.name})));
            TownHttp.RequireSuccess(created);Guard(local,stamp);
            if(local.ownerUid==""){local.ownerUid=session.Uid;saveNow();}
            var meta=JsonUtility.FromJson<RemoteWorld>(created.Text);
            if(local.pending==null&&!local.dirty){Status=meta.headRevisionId==local.lastSyncedRevisionId?"Zapisano w chmurze":"Nowszy zapis w chmurze — możesz go wczytać";return;}
            if(local.pending==null)
            {
                WorldValidation.Validate(local.world);string json=JsonUtility.ToJson(local.world);
                long generation=local.generation;var bytes=await Task.Run(()=>SnapshotBytes.Gzip(json));Guard(local,stamp);
                local.pending=new PendingUpload{key=Guid.NewGuid().ToString(),baseRevisionId=local.lastSyncedRevisionId,
                    generation=generation,gzipBase64=Convert.ToBase64String(bytes),sha256=SnapshotBytes.Hash(bytes)};
                saveNow(); // Do not send until the request key and exact bytes are durable.
            }
            var pending=local.pending;
            var previous=await session.Api("GET",Path(local)+"/requests/"+pending.key);Guard(local,stamp);
            Receipt receipt=null;
            if(previous.status==200){var candidate=JsonUtility.FromJson<Receipt>(previous.Text);if(candidate.status!="pending")receipt=candidate;}
            else if(previous.status!=404)TownHttp.RequireSuccess(previous);
            if(receipt==null)
            {
                var result=await session.Api("POST",Path(local)+"/revisions",Convert.FromBase64String(pending.gzipBase64),"application/gzip",pending.key,pending.baseRevisionId??"",pending.sha256);
                Guard(local,stamp);
                if(result.status!=409)TownHttp.RequireSuccess(result);
                receipt=JsonUtility.FromJson<Receipt>(result.Text);
                if(receipt==null||(receipt.status!="committed"&&receipt.status!="conflict"))TownHttp.RequireSuccess(result);
            }
            if(receipt==null||receipt.sha256!=pending.sha256||string.IsNullOrEmpty(receipt.revisionId))throw new CloudException(0,"Nieprawidłowe potwierdzenie zapisu.");
            if(receipt.status=="conflict")
            {local.conflictRevisionId=receipt.revisionId;Status="Dwie wersje świata — obie zachowane";}
            else
            {local.lastSyncedRevisionId=receipt.revisionId;local.dirty=local.generation!=pending.generation;Status=local.dirty?"Zapisano; są jeszcze nowsze lokalne zmiany":"Zapisano w chmurze";}
            local.pending=null;saveNow();
        }
        public async Task ContinueLocal()
        {
            if(Busy)return;Busy=true;
            try{
                var local=current();int stamp=session.Stamp;
                var r=await session.Api("GET",Path(local));TownHttp.RequireSuccess(r);Guard(local,stamp);
                var remote=JsonUtility.FromJson<RemoteWorld>(r.Text);
                if(!string.IsNullOrEmpty(remote.deletedAt))throw new CloudException(410,"Świat zarchiwizowano — zachowaj go jako nową kopię.");
                local.lastSyncedRevisionId=remote.headRevisionId??"";local.conflictRevisionId="";local.pending=null;local.dirty=true;saveNow();
                await PushCore();
            }finally{Busy=false;}
        }
        public async Task<LocalWorld> Download(string worldId,string revisionId=null)
        {
            if(Busy)throw new CloudException(0,"Poczekaj na koniec synchronizacji.");Busy=true;
            try{
                var stamp=session.Stamp;var r=await session.Api("GET","/v1/worlds/"+worldId);TownHttp.RequireSuccess(r);
                var meta=JsonUtility.FromJson<RemoteWorld>(r.Text);
                if(!string.IsNullOrEmpty(meta.deletedAt))throw new CloudException(410,"Świat zarchiwizowano. Zachowaj lokalną kopię.");
                var rev=revisionId??meta.headRevisionId;if(string.IsNullOrEmpty(rev))throw new CloudException(0,"Ten świat nie ma jeszcze zapisu w chmurze.");
                var data=await session.Api("GET","/v1/worlds/"+worldId+"/revisions/"+rev+"/snapshot");TownHttp.RequireSuccess(data);
                if(data.bytes.Length>SnapshotBytes.MaxCompressed||SnapshotBytes.Hash(data.bytes)!=data.hash)throw new CloudException(0,"Błędna suma kontrolna zapisu.");
                var json=await Task.Run(()=>SnapshotBytes.Ungzip(data.bytes));
                if(session.Stamp!=stamp)throw new OperationCanceledException();
                var world=JsonUtility.FromJson<World>(json);WorldValidation.Validate(world);
                if(world.worldId!=worldId)throw new CloudException(0,"Niezgodny identyfikator świata.");
                var old=findLocal(worldId);if(old!=null&&(old.dirty||old.pending!=null))preserve(old.world.Copy());
                if(revisionId!=null){world.worldId=Guid.NewGuid().ToString();world.name="Z historii — "+world.name;if(world.name.Length>80)world.name=world.name.Substring(0,80);
                    return new LocalWorld{world=world};}
                Status="Wczytano zapis z chmury";
                return new LocalWorld{world=world,ownerUid=session.Uid,lastSyncedRevisionId=rev,dirty=false,generation=1};
            }finally{Busy=false;}
        }
    }
}
