using System;
using System.Collections.Generic;
using System.Linq;

namespace Town.Domain
{
    public enum BuildingKind { Road, House, Shop, Park, Cafe, Workshop, Clinic, Tree, Bench, Playground, Pond, Flowers, Sidewalk }
    [Serializable] public sealed class Building
    {
        public string id;
        public int kind, x, z, rotation, level = 1;
        public Building Copy() => (Building)MemberwiseClone();
    }
    [Serializable] public sealed class Family
    {
        public string id, name, homeId;
        public int mood;
        public Family Copy() => (Family)MemberwiseClone();
    }
    [Serializable] public sealed class World
    {
        public int schemaVersion = 1, simulationVersion = 1, contentVersion = 1;
        public string worldId, name;
        public int width = 32, height = 32, seed = 42, money = 4000, speed = 1, totalTenants;
        public long tick;
        public bool paused = true, sandbox;
        public List<Building> buildings = new List<Building>();
        public List<Family> families = new List<Family>();
        public List<string> claimedGoals = new List<string>();
        public World Copy()
        {
            var w = (World)MemberwiseClone();
            w.buildings = buildings.Select(b => b.Copy()).ToList();
            w.families = families.Select(f => f.Copy()).ToList();
            w.claimedGoals = new List<string>(claimedGoals);
            return w;
        }
        public static World Create(string title, bool freeBuild = false)
        {
            var w = new World { worldId = Guid.NewGuid().ToString(), name = title, sandbox = freeBuild };
            for (int x = 0; x <= 8; x++) w.buildings.Add(new Building {
                id = Guid.NewGuid().ToString(), kind = 0, x = x, z = w.height / 2
            });
            return w;
        }
    }
    public sealed class Definition
    {
        public readonly BuildingKind Kind;
        public readonly string Name;
        public readonly int Cost, Maintenance, Radius, BaseTenants, TenantsPerLevel, MaxLevel, UpgradeBaseCost, UpgradeStepCost;
        public Definition(BuildingKind kind, string name, int cost, int maintenance = 0, int radius = 0, int baseTenants = 0, int tenantsPerLevel = 0, int maxLevel = 1, int upgradeBaseCost = 0, int upgradeStepCost = 0)
        { Kind = kind; Name = name; Cost = cost; Maintenance = maintenance; Radius = radius; BaseTenants = baseTenants; TenantsPerLevel = tenantsPerLevel;
            MaxLevel = maxLevel; UpgradeBaseCost = upgradeBaseCost; UpgradeStepCost = upgradeStepCost; }
    }
    public static class Catalog
    {
        public static readonly Definition[] All = {
            new Definition(BuildingKind.Road, "Droga", 15),
            new Definition(BuildingKind.House, "Dom", 180, 2, baseTenants:2, tenantsPerLevel:1, maxLevel:5, upgradeBaseCost:80, upgradeStepCost:35),
            new Definition(BuildingKind.Shop, "Sklep", 250, 5, 8),
            new Definition(BuildingKind.Park, "Park", 100, 2, 6),
            new Definition(BuildingKind.Cafe, "Kawiarnia", 220, 4, 7),
            new Definition(BuildingKind.Workshop, "Pracownia", 300, 4, 12),
            new Definition(BuildingKind.Clinic, "Przychodnia", 400, 8, 10),
            new Definition(BuildingKind.Tree, "Drzewo", 15, 0, 3),
            new Definition(BuildingKind.Bench, "Ławka", 20, 0, 3),
            new Definition(BuildingKind.Playground, "Plac zabaw", 120, 2, 6),
            new Definition(BuildingKind.Pond, "Staw", 140, 1, 5),
            new Definition(BuildingKind.Flowers, "Kwiaty", 10, 0, 2),
            new Definition(BuildingKind.Sidewalk, "Chodnik", 28, 0, maxLevel:1)
        };
        public static Definition Get(int kind) => All[kind];
        public static int TenantCapacity(Building b)
        {
            if (b == null) return 0;
            if (b.kind < 0 || b.kind >= All.Length) return 0;
            return All[b.kind].BaseTenants + Math.Max(0, Math.Max(1, b.level) - 1) * All[b.kind].TenantsPerLevel;
        }
        public static int MaxLevelFor(int kind) => All[kind].MaxLevel;
        public static int UpgradeCost(int kind, int nextLevel)
        {
            var def = All[kind];
            if (nextLevel > def.MaxLevel || def.UpgradeBaseCost == 0) return int.MaxValue;
            return def.UpgradeBaseCost + Math.Max(0, nextLevel - 2) * def.UpgradeStepCost;
        }
        public static bool CanUpgrade(int kind, int currentLevel) => currentLevel < All[kind].MaxLevel;
        public static bool IsRoad(int kind) => kind == (int)BuildingKind.Road;
        public static bool IsPavement(int kind) => kind == (int)BuildingKind.Sidewalk;
        public static bool IsWalkableSurface(int kind) => IsRoad(kind) || IsPavement(kind);
    }
    public static class WorldValidation
    {
        static bool Id(string value)=>Guid.TryParseExact(value,"D",out _)&&value==value.ToLowerInvariant();
        public static void Validate(World w)
        {
            if (w == null || w.schemaVersion != 1 || w.simulationVersion != 1 || w.contentVersion != 1)
                throw new ArgumentException("Nieobsługiwana wersja świata. Zaktualizuj grę; zapis pozostaje nienaruszony.");
            if (!Id(w.worldId) || string.IsNullOrWhiteSpace(w.name) || w.name.Length > 80)
                throw new ArgumentException("Nieprawidłowa nazwa lub identyfikator świata.");
            if (w.width < 16 || w.width > 128 || w.height < 16 || w.height > 128 || w.tick < 0 || w.tick > 9007199254740991L ||
                w.money < 0 || w.money > 1000000000 || (w.speed != 1 && w.speed != 2 && w.speed != 4) || w.seed < 0)
                throw new ArgumentException("Nieprawidłowe parametry świata.");
            if (w.buildings == null || w.families == null || w.claimedGoals == null || w.buildings.Count > 4096 ||
                w.families.Count > 512 || w.claimedGoals.Count > 12) throw new ArgumentException("Świat przekracza limity prototypu.");
            var ids = new HashSet<string>(); var cells = new HashSet<int>();
            foreach (var b in w.buildings)
            {
                if (b == null || !Id(b.id) || !ids.Add(b.id) || b.kind < 0 || b.kind >= Catalog.All.Length ||
                    b.x < 0 || b.x >= w.width || b.z < 0 || b.z >= w.height || b.rotation < 0 || b.rotation > 3 ||
                    !cells.Add(b.z * w.width + b.x)) throw new ArgumentException("Nieprawidłowy lub nakładający się budynek.");
            }
            var homes = new HashSet<string>(w.buildings.Where(b => b.kind == (int)BuildingKind.House).Select(b => b.id));
            var familyIds = new HashSet<string>();
            foreach (var f in w.families)
                if (f == null || !Id(f.id) || !familyIds.Add(f.id) || !homes.Contains(f.homeId) ||
                    string.IsNullOrWhiteSpace(f.name) || f.name.Length > 80 || f.mood < 0 || f.mood > 100)
                    throw new ArgumentException("Nieprawidłowa rodzina.");
            if(w.totalTenants<0) throw new ArgumentException("Nieprawidłowa liczba mieszkańców.");
            if (w.claimedGoals.Distinct().Count() != w.claimedGoals.Count || w.claimedGoals.Any(g => !Goals.All.Any(x => x.Id == g)))
                throw new ArgumentException("Nieprawidłowe cele.");
        }
    }
    public sealed class Goal
    {
        public readonly string Id, Title;
        public readonly int Reward;
        public readonly Func<World, bool> Complete;
        public Goal(string id, string title, int reward, Func<World, bool> complete)
        { Id = id; Title = title; Reward = reward; Complete = complete; }
    }
    public static class Goals
    {
        static int Count(World w, BuildingKind k) => w.buildings.Count(b => b.kind == (int)k);
        public static readonly Goal[] All = {
            new Goal("homes", "Zbuduj trzy domy", 200, w => Count(w, BuildingKind.House) >= 3),
            new Goal("neighbours", "Przywitaj trzy rodziny", 250, w => w.families.Count >= 3),
            new Goal("shopping", "Otwórz sklep i kawiarnię", 200, w => Count(w, BuildingKind.Shop)>0 && Count(w, BuildingKind.Cafe)>0),
            new Goal("green", "Posadź pięć drzew", 100, w => Count(w, BuildingKind.Tree)>=5),
            new Goal("play", "Zbuduj park i plac zabaw", 180, w => Count(w, BuildingKind.Park)>0 && Count(w, BuildingKind.Playground)>0),
            new Goal("work", "Otwórz dwie pracownie", 250, w => Count(w, BuildingKind.Workshop)>=2),
            new Goal("health", "Otwórz przychodnię", 200, w => Count(w, BuildingKind.Clinic)>0),
            new Goal("water", "Dodaj staw, kwiaty i ławkę", 150, w => Count(w, BuildingKind.Pond)>0 && Count(w, BuildingKind.Flowers)>0 && Count(w, BuildingKind.Bench)>0),
            new Goal("happy", "Osiągnij 75% zadowolenia trzech rodzin", 300, w => w.families.Count>=3 && w.families.Average(f=>f.mood)>=75),
            new Goal("village", "Rozwiń miasteczko do ośmiu rodzin", 500, w => w.families.Count>=8)
        };
    }
    public sealed class Simulation
    {
        public World State { get; private set; }
        readonly Stack<World> undo = new Stack<World>();
        HashSet<int> roadNetwork;
        Dictionary<int, Building> cells;
        static readonly int[] Dx = {1,-1,0,0}, Dz = {0,0,1,-1};
        public int LastBalance { get; private set; }
        public int TotalTenants => State.totalTenants;
        public bool CanUndo => undo.Count > 0;
        public Simulation(World state) { Replace(state); }
        public void Replace(World state) { WorldValidation.Validate(state); State = state; NormalizeState(); undo.Clear(); Invalidate(); }
        void NormalizeState()
        {
            foreach(var b in State.buildings)
                b.level=Math.Max(1,Math.Min(Catalog.MaxLevelFor(b.kind),Math.Max(1,b.level)));
            RecalculateTenants();
        }
        void Invalidate() { cells = null; roadNetwork = null; }
        void Index()
        {
            if (cells != null) return;
            cells = State.buildings.ToDictionary(b => b.z * State.width + b.x);
            roadNetwork = new HashSet<int>(); var q = new Queue<int>();
            foreach (var b in State.buildings.Where(b => b.kind == 0 && b.x == 0))
            { var key=b.z*State.width+b.x; if(roadNetwork.Add(key))q.Enqueue(key); }
            while(q.Count>0)
            {
                int c=q.Dequeue(), x=c%State.width,z=c/State.width;
                for(int i=0;i<4;i++)
                {
                    int nx=x+Dx[i],nz=z+Dz[i],n=nz*State.width+nx;
                    if(nx>=0&&nx<State.width&&nz>=0&&nz<State.height&&cells.TryGetValue(n,out var b)&&b.kind==0&&roadNetwork.Add(n))q.Enqueue(n);
                }
            }
        }
        public Building At(int x,int z) { if(x<0||z<0||x>=State.width||z>=State.height)return null;Index(); cells.TryGetValue(z*State.width+x,out var b); return b; }
        public bool Connected(Building b)
        {
            Index(); if(b.kind==0)return roadNetwork.Contains(b.z*State.width+b.x);
            for(int i=0;i<4;i++)
            { int x=b.x+Dx[i],z=b.z+Dz[i]; if(x>=0&&x<State.width&&z>=0&&z<State.height&&roadNetwork.Contains(z*State.width+x))return true; }
            return false;
        }
        void Remember() { if(undo.Count>=20)undo.Clear(); undo.Push(State.Copy()); }
        public string CanPlace(int kind,int x,int z)
        {
            if(kind<0||kind>=Catalog.All.Length||x<0||x>=State.width||z<0||z>=State.height)return "Poza mapą";
            if(At(x,z)!=null)return "To pole jest zajęte";
            if(State.buildings.Count>=4096)return "Osiągnięto limit budynków";
            if(!State.sandbox&&State.money<Catalog.Get(kind).Cost)return "Brakuje pieniędzy";
            return null;
        }
        public string Place(int kind,int x,int z,int rotation)
        {
            var error=CanPlace(kind,x,z);if(error!=null)return error;
            Remember();State.paused=true;
            if(!State.sandbox)State.money-=Catalog.Get(kind).Cost;
            State.buildings.Add(new Building{id=Guid.NewGuid().ToString(),kind=kind,x=x,z=z,rotation=((rotation%4)+4)%4,level=1});
            RecalculateTenants();
            Invalidate();return null;
        }
        public string Remove(int x,int z)
        {
            var b=At(x,z);if(b==null)return "Wybierz budynek";
            Remember();State.paused=true;
            State.buildings.Remove(b);State.families.RemoveAll(f=>f.homeId==b.id);
            if(!State.sandbox)State.money=Math.Min(1000000000,State.money+Catalog.Get(b.kind).Cost/2);
            RecalculateTenants();
            Invalidate();return null;
        }
        public string Move(string id,int x,int z,int rotation)
        {
            var b=State.buildings.FirstOrDefault(v=>v.id==id);if(b==null)return "Budynek nie istnieje";
            if(x<0||x>=State.width||z<0||z>=State.height||(At(x,z)!=null&&At(x,z)!=b))return "Pole niedostępne";
            Remember();State.paused=true;b.x=x;b.z=z;b.rotation=((rotation%4)+4)%4;Invalidate();return null;
        }
        public string Upgrade(string id)
        {
            var b=State.buildings.FirstOrDefault(v=>v.id==id);
            if(b==null)return "Budynek nie istnieje";
            int current=Math.Max(1,b.level);
            if(!Catalog.CanUpgrade(b.kind,current))return "Budynek osiągnął maksymalny poziom";
            int cost=Catalog.UpgradeCost(b.kind,current+1);
            if(!State.sandbox&&State.money<cost)return "Brakuje pieniędzy";
            Remember();State.paused=true;
            if(!State.sandbox)State.money=Math.Max(0,State.money-cost);
            b.level=current+1;
            RecalculateTenants();
            Invalidate();return null;
        }
        public bool Undo()
        { if(undo.Count==0)return false;State=undo.Pop();State.paused=true;NormalizeState();Invalidate();return true; }
        public bool Claim(string id)
        {
            var g=Goals.All.FirstOrDefault(v=>v.Id==id);
            if(g==null||State.claimedGoals.Contains(id)||!g.Complete(State))return false;
            // Reward changes invalidate edit history, preventing repeated reward collection through undo.
            undo.Clear();State.claimedGoals.Add(id);State.money=Math.Min(1000000000,State.money+g.Reward);return true;
        }
        public bool Step()
        {
            if(State.paused)return false;
            undo.Clear();State.tick++;
            if(State.tick%50!=0)return true;
            Index();
            foreach(var home in State.buildings.Where(b=>b.kind==(int)BuildingKind.House&&Connected(b)))
            {
                if(State.families.Count>=512)break;
                if(Occupancy(home.id) < Math.Max(1, Capacity(home)))
                {
                    string[] names={"Rodzina Agi","Zosia i Leon","Rodzina Nowaków","Maja i Tomek","Ola i Kuba","Julia i Adam","Iga i Filip","Hania i Jan"};
                    State.seed=(int)(((long)State.seed*1103515245+12345)&0x7fffffff);
                    State.families.Add(new Family{id=Guid.NewGuid().ToString(),homeId=home.id,name=names[State.seed%names.Length],mood=50});
                }
            }
            foreach(var f in State.families)
            {
                var home=State.buildings.First(b=>b.id==f.homeId);
                int mood=Connected(home)?40:10;
                foreach(var k in new[]{BuildingKind.Shop,BuildingKind.Park,BuildingKind.Workshop,BuildingKind.Cafe,BuildingKind.Clinic})
                {
                    var def=Catalog.Get((int)k);
                    if(State.buildings.Any(b=>b.kind==(int)k&&Connected(b)&&Math.Abs(b.x-home.x)+Math.Abs(b.z-home.z)<=def.Radius))mood+=12;
                }
                f.mood=Math.Max(0,Math.Min(100,mood));
            }
            int income=State.families.Sum(f=>8+f.mood/10), expense=State.buildings.Sum(ExpenseFor);
            State.totalTenants=Math.Max(0,State.families.Count);
            LastBalance=income-expense;
            if(!State.sandbox)State.money=(int)Math.Max(0,Math.Min(1000000000,(long)State.money+LastBalance));
            return true;
        }
        public int Occupancy(string homeId)=>State.families.Count(f=>f.homeId==homeId);
        public int Occupancy(Building home)=>home==null?0:Occupancy(home.id);
        public int Capacity(Building home)=>home==null?0:Catalog.TenantCapacity(home);
        int ExpenseFor(Building b)
        {
            if(b==null||b.kind<0||b.kind>=Catalog.All.Length) return 0;
            return Catalog.Get(b.kind).Maintenance * Math.Max(1,b.level);
        }
        void RecalculateTenants()=>State.totalTenants=Math.Max(0,State.families.Count);
    }
}
