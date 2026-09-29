using System;
using System.Collections.Generic;
using System.Linq;
using Town.Domain;
using UnityEngine;

namespace Town.Runtime
{
    public sealed class TownView:MonoBehaviour
    {
        readonly Dictionary<string,GameObject> buildings=new Dictionary<string,GameObject>();
        readonly Dictionary<int,Material> materials=new Dictionary<int,Material>();
        readonly Dictionary<string,Citizen> citizens=new Dictionary<string,Citizen>();
        readonly Dictionary<string,Vehicle> vehicles=new Dictionary<string,Vehicle>();
        Material basis,line;GameObject terrain,ghost,selection;Mesh roof,gridMesh;string layout="";
        readonly List<int> roadKeys=new List<int>(); 
        public Camera Camera{get;private set;}
        public Vector3 Target;public float Zoom=15,Yaw=0;
        int width,height;
        float carSpawn,truckSpawn;
        const float CarSpawnEvery=9f, TruckSpawnEvery=18f;const int MaxCars=3, MaxTrucks=1;
        sealed class Citizen{public GameObject go;public List<Vector3> route;public float phase;}
        sealed class Vehicle{public GameObject go;public List<Vector3> route;public float phase;public float speed;public bool truck;}
        readonly Color[] colors={new Color(.22f,.29f,.31f),new Color(.91f,.74f,.49f),new Color(.40f,.65f,.76f),new Color(.34f,.62f,.41f),
            new Color(.86f,.51f,.42f),new Color(.55f,.58f,.69f),new Color(.86f,.90f,.88f),new Color(.22f,.49f,.32f),
            new Color(.58f,.36f,.22f),new Color(.91f,.64f,.28f),new Color(.29f,.63f,.75f),new Color(.84f,.40f,.60f),new Color(.56f,.56f,.58f)};
        Color ColorForKind(int kind)
        { return kind>=0&&kind<colors.Length?colors[kind]:new Color(.5f,.55f,.58f);}
        public void Initialize(Material basis,Material line)
        {
            this.basis=basis;this.line=line;
            var cameraObject=new GameObject("Town camera",typeof(Camera),typeof(AudioListener));cameraObject.transform.SetParent(transform);
            Camera=cameraObject.GetComponent<Camera>();Camera.orthographic=true;Camera.nearClipPlane=.1f;Camera.farClipPlane=180;
            Camera.clearFlags=CameraClearFlags.SolidColor;Camera.backgroundColor=new Color(.71f,.80f,.79f);Camera.tag="MainCamera";
            var sun=new GameObject("Sun",typeof(Light));sun.transform.SetParent(transform);sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var light=sun.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.shadows=LightShadows.Soft;
            RenderSettings.ambientLight=new Color(.64f,.68f,.73f);
            ghost=Primitive("Placement",PrimitiveType.Cube,transform,Vector3.zero,new Vector3(.98f,.045f,.98f),Material(20,new Color(.35f,.85f,.61f)));
            selection=Primitive("Selection",PrimitiveType.Cylinder,transform,Vector3.zero,new Vector3(1.04f,.015f,1.04f),Material(21,new Color(1,.83f,.29f)));
            ghost.SetActive(false);selection.SetActive(false);
            roof=new Mesh();roof.name="Shared roof";
            roof.vertices=new[]{new Vector3(-.44f,0,-.44f),new Vector3(.44f,0,-.44f),new Vector3(0,.35f,-.44f),
                new Vector3(-.44f,0,.44f),new Vector3(.44f,0,.44f),new Vector3(0,.35f,.44f)};
            roof.triangles=new[]{0,2,1,3,4,5,0,3,5,0,5,2,1,2,5,1,5,4,0,1,4,0,4,3};roof.RecalculateNormals();
        }
        Material Material(int id,Color color)
        {if(!materials.TryGetValue(id,out var m)){m=new Material(basis){color=color,enableInstancing=true};materials[id]=m;}return m;}
        GameObject Primitive(string name,PrimitiveType type,Transform parent,Vector3 p,Vector3 scale,Material mat)
        {var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;
         g.GetComponent<Renderer>().sharedMaterial=mat;var collider=g.GetComponent<Collider>();if(collider)Destroy(collider);return g;}
        void Tree(Transform parent,float x,float z,float size=1)
        {Primitive("Trunk",PrimitiveType.Cylinder,parent,new Vector3(x,.25f*size,z),new Vector3(.12f,.25f,.12f)*size,Material(30,new Color(.42f,.28f,.17f)));
         Primitive("Crown",PrimitiveType.Sphere,parent,new Vector3(x,.64f*size,z),new Vector3(.65f,.8f,.65f)*size,Material(7,colors[7]));}
        GameObject BuildingObject(Building b)
        {
            var root=new GameObject(Catalog.Get(b.kind).Name);root.transform.SetParent(transform,false);var t=root.transform;var mat=Material(b.kind,ColorForKind(b.kind));
            if(b.kind==0){Primitive("Road",PrimitiveType.Cube,t,new Vector3(0,.035f,0),new Vector3(.98f,.06f,.98f),mat);}
            else if(b.kind==12){Primitive("Sidewalk",PrimitiveType.Cube,t,new Vector3(0,.035f,0),new Vector3(.98f,.05f,.98f),mat);}
            else if(b.kind==7)Tree(t,0,0);
            else if(b.kind==10)Primitive("Water",PrimitiveType.Sphere,t,new Vector3(0,.03f,0),new Vector3(.95f,.08f,.85f),mat);
            else if(b.kind==8){Primitive("Bench",PrimitiveType.Cube,t,new Vector3(0,.2f,0),new Vector3(.7f,.1f,.25f),mat);
                Primitive("Back",PrimitiveType.Cube,t,new Vector3(0,.35f,.12f),new Vector3(.7f,.22f,.06f),mat);}
            else if(b.kind==11){for(int i=0;i<4;i++)Primitive("Flower",PrimitiveType.Sphere,t,new Vector3((i%2-.5f)*.35f,.12f,(i/2-.5f)*.35f),Vector3.one*.24f,mat);}
            else if(b.kind==3||b.kind==9){Primitive("Garden",PrimitiveType.Cube,t,new Vector3(0,.025f,0),new Vector3(.94f,.05f,.94f),Material(3,colors[3]));Tree(t,-.2f,.15f,.65f);
                Primitive("Play / seating",PrimitiveType.Cube,t,new Vector3(.2f,.15f,-.2f),new Vector3(.35f,.25f,.4f),mat);}
            else{
                float h=b.kind==5?0.9f:0.65f;
                Primitive("Facade",PrimitiveType.Cube,t,new Vector3(0,h/2,0),new Vector3(.76f,h,.76f),mat);
                var roofObject=new GameObject("Roof",typeof(MeshFilter),typeof(MeshRenderer));roofObject.transform.SetParent(t,false);roofObject.transform.localPosition=new Vector3(0,h,0);
                roofObject.GetComponent<MeshFilter>().sharedMesh=roof;roofObject.GetComponent<MeshRenderer>().sharedMaterial=Material(31,new Color(.49f,.29f,.25f));
                var windows=Material(32,new Color(.18f,.37f,.43f));
                Primitive("Window",PrimitiveType.Cube,t,new Vector3(-.19f,.37f,-.391f),new Vector3(.18f,.2f,.025f),windows);
                Primitive("Door",PrimitiveType.Cube,t,new Vector3(.18f,.2f,-.391f),new Vector3(.18f,.38f,.025f),windows);
                if(b.kind==6){var cross=Material(33,new Color(.82f,.25f,.24f));Primitive("Cross",PrimitiveType.Cube,t,new Vector3(0,.65f,-.42f),new Vector3(.3f,.08f,.03f),cross);
                    Primitive("Cross",PrimitiveType.Cube,t,new Vector3(0,.65f,-.42f),new Vector3(.08f,.3f,.03f),cross);}
            }
            return root;
        }
        public void ResetWorld(World world)
        {
            foreach(var g in buildings.Values)Destroy(g);buildings.Clear();foreach(var c in citizens.Values)Destroy(c.go);citizens.Clear();
            foreach(var v in vehicles.Values)Destroy(v.go);vehicles.Clear();
            if(terrain)Destroy(terrain);if(gridMesh)Destroy(gridMesh);layout="";width=world.width;height=world.height;Target=new Vector3(width/2f,0,height/2f);Zoom=Mathf.Max(width,height)*.48f;Yaw=0;
            terrain=new GameObject("Terrain");terrain.transform.SetParent(transform);
            Primitive("Ground",PrimitiveType.Cube,terrain.transform,new Vector3((width-1)/2f,-.15f,(height-1)/2f),new Vector3(width,.25f,height),Material(40,new Color(.62f,.72f,.51f)));
            var vertices=new List<Vector3>();var indices=new List<int>();
            for(int x=0;x<=width;x++){indices.Add(vertices.Count);vertices.Add(new Vector3(x-.5f,-.014f,-.5f));indices.Add(vertices.Count);vertices.Add(new Vector3(x-.5f,-.014f,height-.5f));}
            for(int z=0;z<=height;z++){indices.Add(vertices.Count);vertices.Add(new Vector3(-.5f,-.014f,z-.5f));indices.Add(vertices.Count);vertices.Add(new Vector3(width-.5f,-.014f,z-.5f));}
            var grid=new GameObject("Grid",typeof(MeshFilter),typeof(MeshRenderer));grid.transform.SetParent(terrain.transform);
            var mesh=gridMesh=new Mesh();mesh.SetVertices(vertices);mesh.SetIndices(indices.ToArray(),MeshTopology.Lines,0);grid.GetComponent<MeshFilter>().sharedMesh=mesh;grid.GetComponent<MeshRenderer>().sharedMaterial=line;
            ApplyCamera();
        }
        public void Reconcile(World w,Simulation sim)
        {
            var existing=new HashSet<string>(w.buildings.Select(b=>b.id));
            foreach(var id in buildings.Keys.Where(id=>!existing.Contains(id)).ToArray()){Destroy(buildings[id]);buildings.Remove(id);}
            foreach(var b in w.buildings)
            {if(!buildings.TryGetValue(b.id,out var obj)){obj=BuildingObject(b);buildings[b.id]=obj;}
             obj.transform.position=new Vector3(b.x,0,b.z);obj.transform.rotation=Quaternion.Euler(0,b.rotation*90,0);}
            var nextLayout=string.Join(";",w.buildings.Select(b=>b.id+":"+b.x+":"+b.z));bool reroute=nextLayout!=layout;layout=nextLayout;
            roadKeys.Clear();roadKeys.AddRange(w.buildings.Where(b=>b.kind==(int)BuildingKind.Road).Select(b=>b.z*w.width+b.x));
            if(reroute)ClearVehicles();
            var visible=w.families.Take(48).ToArray();var familyIds=new HashSet<string>(visible.Select(f=>f.id));
            foreach(var id in citizens.Keys.Where(id=>!familyIds.Contains(id)).ToArray()){Destroy(citizens[id].go);citizens.Remove(id);}
            foreach(var f in visible)
            {
                var home=w.buildings.First(b=>b.id==f.homeId);
                if(!citizens.TryGetValue(f.id,out var c)){c=new Citizen{go=Primitive("Resident",PrimitiveType.Capsule,transform,Vector3.zero,new Vector3(.16f,.22f,.16f),Material(50,new Color(.91f,.67f,.32f)))};citizens[f.id]=c;}
                if(reroute||c.route==null){c.route=Route(w,home);c.phase=0;c.go.transform.position=c.route[0];}
            }
        }
        List<Vector3> Route(World w,Building home)
        {
            var walkable = w.buildings.Where(b=>Catalog.IsWalkableSurface(b.kind)).ToDictionary(b=>b.z*w.width+b.x,b=>b);
            if(walkable.Count==0)return new List<Vector3>{new Vector3(home.x,.24f,home.z-.4f)};
            int source = FindStartOnSurface(walkable,home,w.width,w.height);
            if(source < 0)return new List<Vector3>{new Vector3(home.x,.24f,home.z-.4f)};
            return BuildRoundTripRoute(w, walkable, source, -1, true);
        }
        int FindStartOnSurface(Dictionary<int,Building> walkable,Building home,int width,int height)
        {
            for(int i=0;i<4;i++)
            {
                int nx=home.x+(i==0?1:i==1?-1:0), nz=home.z+(i==2?1:i==3?-1:0);
                int key=nz*width+nx;
                if(nx>=0&&nx<width&&nz>=0&&nz<height&&walkable.ContainsKey(key)) return key;
            }
            return walkable.Count>0 ? walkable.Keys.First() : -1;
        }
        List<Vector3> BuildRoundTripRoute(World w,Dictionary<int,Building> nodes,int source,int destination,bool loopBack)
        {
            var path=BuildPath(w,nodes,source,destination,true);
            if(path==null||path.Count==0)return new List<Vector3>{new Vector3(source%w.width,.24f,source/w.width),new Vector3(source%w.width,.24f,source/w.width)};
            var route = path.Select(node => new Vector3(node%w.width,.24f,node/w.width)).ToList();
            if(loopBack && route.Count>1)
            {
                var back=route.Take(route.Count-1).Reverse().ToArray();
                route.AddRange(back);
            }
            return route;
        }
        List<Vector3> BuildRouteForVehicle(int source,int destination,bool truck)
        {
            if(!roadKeys.Contains(source)||!roadKeys.Contains(destination))return null;
            var walkable=roadKeys.ToDictionary(k=>k,new Building{kind=(int)BuildingKind.Road,x=k%width,z=k/width});
            var path=BuildPath(new World {width=width,height=height,buildings=walkable.Values.ToList()},walkable,source,destination);
            if(path==null||path.Count==0)return null;
            float y=truck?0.08f:0.06f;
            var route=path.Select(k=>new Vector3(k%width,y,k/width)).ToList();
            var back=route.Take(route.Count-1).Reverse().ToArray();
            route.AddRange(back);
            return route;
        }
        List<int> BuildPath(World w,Dictionary<int,Building> nodes,int source,int destination,bool forPedestrians=false)
        {
            if(!nodes.ContainsKey(source)||(destination>=0&&!nodes.ContainsKey(destination)))return null;
            var came = new Dictionary<int,int>{{source,-1}};
            var q=new Queue<int>();q.Enqueue(source);int far=source;
            while(q.Count>0)
            {
                int key=q.Dequeue(),x=key%w.width,z=key/w.width;
                if(!forPedestrians && key==destination){far=key;break;}
                var neighbors=new[]{(x+1,z),(x-1,z),(x,z+1),(x,z-1)};
                foreach(var pair in neighbors)
                {
                    int nx=pair.Item1,nz=pair.Item2,n=nz*w.width+nx;
                    if(nx>=0&&nx<w.width&&nz>=0&&nz<w.height&&nodes.ContainsKey(n)&&!came.ContainsKey(n)){came[n]=key;q.Enqueue(n);}
                }
                if(q.Count==0)far=key;
            }
            if(!forPedestrians && !came.ContainsKey(destination))destination=far;
            if(forPedestrians && destination<0)destination=far;
            var route=new List<int>();int current=destination;
            while(current!=-1){route.Add(current);current=came[current];}
            route.Reverse();return route;
        }
        GameObject VehicleObject(bool truck)
        {
            var root=new GameObject(truck?"Truck":"Car");
            root.transform.SetParent(transform,false);
            if(truck){Primitive("Bed",PrimitiveType.Cube,root.transform,Vector3.zero,new Vector3(.68f,.14f,.42f),Material(35,new Color(.3f,.3f,.35f)));
                Primitive("Cabin",PrimitiveType.Cube,root.transform,new Vector3(.18f,.22f,-.06f),new Vector3(.28f,.2f,.24f),Material(36,new Color(.43f,.43f,.45f)));}
            else{Primitive("Body",PrimitiveType.Cube,root.transform,Vector3.zero,new Vector3(.52f,.16f,.28f),Material(34,new Color(.25f,.4f,.35f)));}
            var wheelY=-0.06f;
            float wheelOffset=truck?0.16f:0.11f;
            for(int side=-1;side<=1;side+=2)Primitive("Wheel",PrimitiveType.Cylinder,root.transform,new Vector3(side*wheelOffset,wheelY,0.11f),new Vector3(.1f,.03f,.1f),Material(33,new Color(.1f,.1f,.1f)));
            float lightY=truck?0.09f:0.08f;
            Primitive("Light",PrimitiveType.Sphere,root.transform,new Vector3(0,lightY,.19f),new Vector3(.05f,.05f,.05f),Material(32,new Color(.98f,.78f,.25f)));
            return root;
        }
        int PickRoadKey(bool edgeOnly,int avoid=-1)
        {
            if(roadKeys.Count==0)return -1;
            var candidates=edgeOnly
                ? roadKeys.Where(k=>k%width==0||k%width==width-1||k/width==0||k/width==height-1).ToList()
                : roadKeys;
            if(candidates.Count==0)candidates=roadKeys;
            if(candidates.Count==0)return -1;
            if(candidates.Count==1&&avoid!=-1&&candidates[0]==avoid)return -1;
            int pick;
            int attempts=0;
            do{pick=candidates[UnityEngine.Random.Range(0,candidates.Count)];attempts++;}while(pick==avoid&&attempts<8);
            return pick;
        }
        public void Animate(float delta)
        {
            carSpawn+=delta;
            truckSpawn+=delta;
            while(carSpawn>=CarSpawnEvery && vehicles.Count(v=>!v.Value.truck)<MaxCars){carSpawn-=CarSpawnEvery;TrySpawnVehicle(false);}
            while(truckSpawn>=TruckSpawnEvery && vehicles.Count(v=>v.Value.truck)<MaxTrucks){truckSpawn-=TruckSpawnEvery;TrySpawnVehicle(true);}
            foreach(var c in citizens.Values) AnimateWalking(c,delta);
            foreach(var vehicle in vehicles.Values) AnimateVehicle(vehicle,delta);
        }
        void AnimateWalking(Citizen citizen,float delta)
        {
            if(citizen.route.Count<2) return;
            citizen.phase=(citizen.phase+delta*.8f)%(citizen.route.Count-1);
            int a=(int)citizen.phase;
            float t=citizen.phase-a;
            citizen.go.transform.position=Vector3.Lerp(citizen.route[a],citizen.route[a+1],t);
        }
        void AnimateVehicle(Vehicle vehicle,float delta)
        {
            if(vehicle.route.Count<2) return;
            vehicle.phase=(vehicle.phase+delta*vehicle.speed)%(vehicle.route.Count-1);
            int a=(int)vehicle.phase;
            float t=vehicle.phase-a;
            var aPoint=vehicle.route[a];var bPoint=vehicle.route[a+1];
            vehicle.go.transform.position=Vector3.Lerp(aPoint,bPoint,t);
            var look=(bPoint-aPoint);look.y=0;
            if(look.sqrMagnitude>0.0001f) vehicle.go.transform.rotation=Quaternion.LookRotation(look.normalized);
        }
        void TrySpawnVehicle(bool truck)
        {
            if(roadKeys.Count<2) return;
            int source=PickRoadKey(true);
            if(source==-1)return;
            int destination=PickRoadKey(false,source);
            if(destination==-1||source==destination)return;
            var route=BuildRouteForVehicle(source,destination,truck);
            if(route==null||route.Count<2)return;
            var id=Guid.NewGuid().ToString();
            var go=VehicleObject(truck);
            vehicles[id]=new Vehicle{go=go,route=route,phase=(float)UnityEngine.Random.Range(0f,route.Count-1),speed=truck?0.5f:1f,truck=truck};
            go.transform.position=route[0];
        }
        void ClearVehicles(){foreach(var v in vehicles.Values)Destroy(v.go);vehicles.Clear();}
        public void ApplyCamera()
        {Target.x=Mathf.Clamp(Target.x,0,width);Target.z=Mathf.Clamp(Target.z,0,height);Zoom=Mathf.Clamp(Zoom,3,Mathf.Max(width,height));Camera.orthographicSize=Zoom;
         Camera.transform.position=Target+Quaternion.Euler(50,Yaw,0)*new Vector3(0,0,-60);Camera.transform.LookAt(Target);}
        public bool Ground(Vector2 screen,out Vector3 hit)
        {var ray=Camera.ScreenPointToRay(screen);var plane=new Plane(Vector3.up,Vector3.zero);if(plane.Raycast(ray,out var distance)){hit=ray.GetPoint(distance);return true;}hit=Vector3.zero;return false;}
        public void Preview(int x,int z,bool valid,bool visible)
        {ghost.SetActive(visible);ghost.transform.position=new Vector3(x,.035f,z);ghost.GetComponent<Renderer>().sharedMaterial=valid?Material(20,new Color(.35f,.85f,.61f)):Material(22,new Color(.95f,.35f,.35f));}
        public void Select(Building b){selection.SetActive(b!=null);if(b!=null)selection.transform.position=new Vector3(b.x,.04f,b.z);}
        void OnDestroy(){foreach(var m in materials.Values)Destroy(m);if(roof)Destroy(roof);if(gridMesh)Destroy(gridMesh);}
    }
}
