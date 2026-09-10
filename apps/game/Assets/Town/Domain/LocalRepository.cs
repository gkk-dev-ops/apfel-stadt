using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Town.Domain
{
    [Serializable] public sealed class PendingUpload
    {
        public string key, baseRevisionId, sha256, gzipBase64;
        public long generation;
    }
    [Serializable] public sealed class LocalWorld
    {
        public World world;
        public string ownerUid="", lastSyncedRevisionId="", conflictRevisionId="";
        public bool dirty=true;
        public long generation=1;
        public PendingUpload pending;
    }
    public static class SnapshotBytes
    {
        public const int MaxCompressed=8*1024*1024, MaxDecoded=16*1024*1024;
        public static string Hash(byte[] bytes)
        { using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        public static byte[] Gzip(string json)
        {
            var bytes=Encoding.UTF8.GetBytes(json);if(bytes.Length>MaxDecoded)throw new InvalidDataException("Zapis jest za duży.");
            using(var output=new MemoryStream())
            { using(var zip=new GZipStream(output,CompressionLevel.Optimal,true))zip.Write(bytes,0,bytes.Length);
              var result=output.ToArray();if(result.Length>MaxCompressed)throw new InvalidDataException("Zapis jest za duży.");return result; }
        }
        public static string Ungzip(byte[] bytes,int decodedLimit=MaxDecoded,int compressedLimit=MaxCompressed)
        {
            if(bytes==null||bytes.Length>compressedLimit)throw new InvalidDataException("Zapis jest za duży.");
            using(var input=new MemoryStream(bytes))using(var zip=new GZipStream(input,CompressionMode.Decompress))using(var output=new MemoryStream())
            { var buffer=new byte[8192];int n;while((n=zip.Read(buffer,0,buffer.Length))>0)
              { if(output.Length+n>decodedLimit)throw new InvalidDataException("Zapis jest za duży po rozpakowaniu.");output.Write(buffer,0,n); }
              return new UTF8Encoding(false,true).GetString(output.ToArray()); }
        }
    }
    // JSON serialization is injected: Unity JsonUtility in game, System.Text.Json in headless tests.
    public sealed class LocalRepository
    {
        readonly string directory; readonly object gate=new object();
        readonly Dictionary<string,long> written=new Dictionary<string,long>();
        readonly Func<string,LocalWorld> decode;
        public LocalRepository(string directory,Func<string,LocalWorld> decode)
        { this.directory=directory;this.decode=decode;Directory.CreateDirectory(directory); }
        string PathFor(string id)
        { if(!Guid.TryParseExact(id,"D",out _))throw new ArgumentException("Invalid world ID");return Path.Combine(directory,id+".town"); }
        public string FilePath(string id)=>PathFor(id);
        public void Write(string id,string capturedJson,long sequence)
        {
            // A local envelope can carry a base64 in-flight snapshot as well as the current world.
            var bytes=Encoding.UTF8.GetBytes(capturedJson);if(bytes.Length>48*1024*1024)throw new InvalidDataException("Local save too large");
            byte[] payload;
            using(var output=new MemoryStream())
            { using(var zip=new GZipStream(output,CompressionLevel.Optimal,true))zip.Write(bytes,0,bytes.Length);payload=output.ToArray(); }
            if(payload.Length+71>24*1024*1024)throw new InvalidDataException("Local file too large");
            var header=Encoding.ASCII.GetBytes("TOWN1\n"+SnapshotBytes.Hash(payload)+"\n");
            lock(gate)
            {
                if(written.TryGetValue(id,out var last)&&sequence<last)return;
                var dest=PathFor(id);var temp=dest+".tmp";
                using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
                { file.Write(header,0,header.Length);file.Write(payload,0,payload.Length);file.Flush(true); }
                if(File.Exists(dest))File.Replace(temp,dest,dest+".bak");else File.Move(temp,dest);
                written[id]=sequence;
            }
        }
        LocalWorld ReadFile(string path)
        {
            var info=new FileInfo(path);if(info.Length>24*1024*1024)throw new InvalidDataException("Local file too large");
            var bytes=File.ReadAllBytes(path);
            if(bytes.Length<71||Encoding.ASCII.GetString(bytes,0,6)!="TOWN1\n"||bytes[70]!=10)throw new InvalidDataException("Invalid local header");
            var payload=new byte[bytes.Length-71];Array.Copy(bytes,71,payload,0,payload.Length);
            if(SnapshotBytes.Hash(payload)!=Encoding.ASCII.GetString(bytes,6,64))throw new InvalidDataException("Checksum mismatch");
            LocalWorld local;
            try { local=decode(SnapshotBytes.Ungzip(payload,48*1024*1024,24*1024*1024)); }
            catch(Exception e) { throw new InvalidDataException("Invalid local JSON",e); }
            if(local==null)throw new InvalidDataException("Invalid save");WorldValidation.Validate(local.world);
            local.ownerUid=local.ownerUid??"";local.lastSyncedRevisionId=local.lastSyncedRevisionId??"";local.conflictRevisionId=local.conflictRevisionId??"";
            if(local.pending!=null&&string.IsNullOrEmpty(local.pending.key))local.pending=null;
            if(local.pending!=null) {
                var p=local.pending;
                if(!Guid.TryParseExact(p.key,"D",out _)||p.generation<0||p.generation>local.generation||string.IsNullOrEmpty(p.gzipBase64)||p.gzipBase64.Length>12*1024*1024)
                    throw new InvalidDataException("Invalid pending upload");
                p.baseRevisionId=p.baseRevisionId??"";
                if(p.baseRevisionId!=""&&!Guid.TryParseExact(p.baseRevisionId,"D",out _))throw new InvalidDataException("Invalid pending base");
                byte[] pendingBytes;try{pendingBytes=Convert.FromBase64String(p.gzipBase64);}catch(FormatException e){throw new InvalidDataException("Invalid pending bytes",e);}
                if(pendingBytes.Length>SnapshotBytes.MaxCompressed||SnapshotBytes.Hash(pendingBytes)!=p.sha256)throw new InvalidDataException("Invalid pending checksum");
            }
            if(local.generation<0)throw new InvalidDataException("Invalid save generation");
            return local;
        }
        public LocalWorld Load(string id)
        {
            lock(gate)
            {
                var path=PathFor(id);
                try { var value=ReadFile(path);if(value.world.worldId!=id)throw new InvalidDataException("ID mismatch");return value; }
                catch(Exception e) when(e is IOException||e is ArgumentException||e is InvalidDataException)
                { var value=ReadFile(path+".bak");if(value.world.worldId!=id)throw new InvalidDataException("Backup ID mismatch");return value; }
            }
        }
        public string[] ListIds()=>Directory.GetFiles(directory,"*.town").Select(Path.GetFileNameWithoutExtension).ToArray();
        public LocalWorld ImportAsCopy(string path)
        {
            var source=ReadFile(path);
            source.world.worldId=Guid.NewGuid().ToString();source.world.name="Kopia — "+source.world.name;
            if(source.world.name.Length>80)source.world.name=source.world.name.Substring(0,80);
            source.ownerUid="";source.lastSyncedRevisionId="";source.conflictRevisionId="";
            source.pending=null;source.dirty=true;source.generation=1;
            return source;
        }
    }
}
