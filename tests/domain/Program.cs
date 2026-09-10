using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Town.Domain;
static class Program
{
    static int passed;
    static JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
    static void Reject(Action action,string message){try{action();}catch(ArgumentException){passed++;return;}throw new Exception(message);}
    static void Main(string[] args)
    {
        var world=World.Create("Nasze Miasteczko");var sim=new Simulation(world);int z=world.height/2;
        var cash=world.money;Check(sim.Place(1,3,z+1,0)==null,"place");Check(world.money==cash-180,"atomic cost");
        Check(sim.Place(1,3,z+1,0)!=null&&world.money==cash-180,"overlap does not charge");
        Check(sim.Place(1,-1,z,0)!=null,"map bounds");
        Check(sim.Connected(sim.At(3,z+1)),"house has road");
        Check(sim.Move(sim.At(3,z+1).id,4,z+1,1)==null,"move");Check(sim.Undo()&&sim.At(3,z+1)!=null,"undo move");
        world=sim.State;world.paused=false;for(int i=0;i<50;i++)sim.Step();
        Check(world.families.Count==1,"family moves in");Check(!sim.CanUndo,"simulation invalidates edit undo");
        sim.Remove(3,z+1);Check(world.families.Count==0,"remove household with home");
        Check(sim.Undo()&&sim.State.families.Count==1,"undo demolition restores household");
        world=sim.State;sim.Place(1,4,z+1,0);sim.Place(1,5,z+1,0);
        Check(sim.Claim("homes"),"claim goal");Check(!sim.Claim("homes")&&!sim.CanUndo,"reward cannot be duplicated through undo");
        sim.Remove(0,z);Check(!sim.Connected(sim.At(3,z+1)),"disconnect entrance");
        var copy=world.Copy();copy.buildings[0].kind=7;Check(world.buildings[0].kind!=7,"deep copy");
        var invalid=world.Copy();invalid.buildings.Add(invalid.buildings[0].Copy());Reject(()=>WorldValidation.Validate(invalid),"duplicate accepted");
        invalid=world.Copy();invalid.schemaVersion=99;Reject(()=>WorldValidation.Validate(invalid),"future version accepted");
        invalid=world.Copy();invalid.families[0].homeId=Guid.NewGuid().ToString();Reject(()=>WorldValidation.Validate(invalid),"orphan accepted");
        var path=Path.Combine(Path.GetTempPath(),"town-domain-"+Guid.NewGuid());Directory.CreateDirectory(path);
        try{
            var repo=new LocalRepository(path,s=>JsonSerializer.Deserialize<LocalWorld>(s,options));
            var env=new LocalWorld{world=world.Copy()};string json=JsonSerializer.Serialize(env,options);
            repo.Write(world.worldId,json,1);var read=repo.Load(world.worldId);Check(read.world.money==world.money,"save roundtrip");
            env.world.money+=10;repo.Write(world.worldId,JsonSerializer.Serialize(env,options),3);
            repo.Write(world.worldId,json,2);Check(repo.Load(world.worldId).world.money==env.world.money,"old async save cannot replace new");
            File.WriteAllText(repo.FilePath(world.worldId),"broken");Check(repo.Load(world.worldId).world.money==world.money,"corrupt save uses backup");
            var imported=repo.ImportAsCopy(repo.FilePath(world.worldId)+".bak");Check(imported.world.worldId!=world.worldId&&imported.ownerUid=="","import forks identity");
            var pending=SnapshotBytes.Gzip(JsonSerializer.Serialize(env.world,options));env.generation=4;
            env.pending=new PendingUpload{key=Guid.NewGuid().ToString(),gzipBase64=Convert.ToBase64String(pending),sha256=SnapshotBytes.Hash(pending),generation=4};
            repo.Write(world.worldId,JsonSerializer.Serialize(env,options),4);Check(repo.Load(world.worldId).pending.key==env.pending.key,"pending upload durable");
        }finally{Directory.Delete(path,true);}
        string serialized=JsonSerializer.Serialize(world,options);Check(SnapshotBytes.Ungzip(SnapshotBytes.Gzip(serialized))==serialized,"gzip roundtrip");
        if(args.Length>0){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));File.WriteAllText(args[0],serialized);}
        Console.WriteLine($"{passed} domain/persistence assertions passed.");
    }
}
