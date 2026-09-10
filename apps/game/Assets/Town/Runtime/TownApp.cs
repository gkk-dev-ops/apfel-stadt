using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Town.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Town.Runtime
{
    public sealed class TownApp:MonoBehaviour
    {
        public Material worldMaterial,gridMaterial;
        enum Panel{None,Worlds,Cloud,Goals,Settings,History}
        LocalRepository repo;LocalWorld local;Simulation sim;TownView view;TownSession session;CloudSync cloud;
        Panel panel;bool uiBusy,eco;int selectedKind=-1,rotation;string selectedId="",movingId="";
        string toast="",email="",password="",newName="Nowe miasteczko",cloudCursor="",historyCursor="";
        float toastUntil,accumulator,saveTimer,syncTimer,thermalTimer,healthySeconds;long writeSequence;
        bool uiScrollDragged;Vector2 uiTouchStart;
        int qualityLevel=-1;bool fingerSequence,dragged,held;Vector2 down,lastPointer;float lastPinch;
        Vector2 catalogScroll,listScroll;List<LocalWorld> localList=new List<LocalWorld>();
        readonly List<RemoteWorld> remoteList=new List<RemoteWorld>();readonly List<RemoteRevision> history=new List<RemoteRevision>();
        GUIStyle label,small,title,button,activeButton,box,field;readonly List<Texture2D> textures=new List<Texture2D>();
        AudioSource audioSource;AudioClip clickClip,goalClip;UniversalRenderPipelineAsset pipeline;
        public LocalWorld Current=>local;
        float UiHeight=>Application.isMobilePlatform?540:720;
        float Scale=>Screen.height/UiHeight;
        float W=>Screen.safeArea.width/Scale;
        float H=>Screen.safeArea.height/Scale;
        bool Busy=>uiBusy||(cloud!=null&&cloud.Busy);
        void Awake()
        {
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            repo=new LocalRepository(Path.Combine(Application.persistentDataPath,"Worlds"),s=>JsonUtility.FromJson<LocalWorld>(s));
            var configAsset=Resources.Load<TextAsset>("TownClientConfig");
            var config=configAsset?JsonUtility.FromJson<ClientConfig>(configAsset.text):new ClientConfig();
            session=new TownSession(config);cloud=new CloudSync(session,()=>local,SaveNow,Preserve,FindLocal);
            AppleServices.Haptics=PlayerPrefs.GetInt("haptics",1)!=0;AppleServices.Intensity=PlayerPrefs.GetFloat("haptic-strength",.55f);
            eco=PlayerPrefs.GetInt("eco",0)!=0;
            view=gameObject.AddComponent<TownView>();view.Initialize(worldMaterial,gridMaterial);
            var source=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(source){pipeline=Instantiate(source);QualitySettings.renderPipeline=pipeline;}
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.volume=PlayerPrefs.GetFloat("sound",.5f);
            clickClip=Tone("Place",520,.06f);goalClip=Tone("Goal",780,.18f);
            RefreshLocal();if(localList.Count>0)Open(localList[0]);else Demo();
            Note("Dotknij obiektu, aby go poznać. Wybierz budynek na dole, aby rozbudować miasto.",9);
        }
        async void Start(){try{await session.RestoreSession();}catch(Exception){/* Local play must not depend on cloud availability. */}}
        AudioClip Tone(string name,float frequency,float duration)
        {int count=(int)(22050*duration);var data=new float[count];for(int i=0;i<count;i++)data[i]=Mathf.Sin(i*frequency*2*Mathf.PI/22050)*.1f*(1-i/(float)count);
         var clip=AudioClip.Create(name,count,1,22050,false);clip.SetData(data,0);return clip;}
        void Note(string message,float seconds=5){toast=message;toastUntil=Time.unscaledTime+seconds;}
        void Feedback(bool good,int kind=0){AppleServices.Pulse(good?kind:1);if(good&&audioSource)audioSource.PlayOneShot(kind==2?goalClip:clickClip);}
        void Mark(){local.world=sim.State;local.generation++;local.dirty=true;}
        void RefreshLocal()
        {localList.Clear();foreach(var id in repo.ListIds())try{localList.Add(repo.Load(id));}catch(Exception){Note("Jeden zapis wymaga odzyskania. Plik pozostaje na urządzeniu.");}}
        void Open(LocalWorld value)
        {
            local=value;sim=new Simulation(local.world);view.ResetWorld(local.world);view.Reconcile(local.world,sim);
            selectedId="";movingId="";selectedKind=-1;accumulator=0;saveTimer=0;syncTimer=0;panel=Panel.None;
        }
        void Demo()
        {
            var world=World.Create("Pierwsza uliczka");var s=new Simulation(world);int z=world.height/2;
            s.Place(1,2,z+1,0);s.Place(1,4,z+1,0);s.Place(2,6,z+1,0);s.Place(3,3,z-1,0);s.Place(5,6,z-1,0);s.Place(7,2,z-2,0);
            world.paused=false;for(int i=0;i<50;i++)s.Step();Open(new LocalWorld{world=world});Try(SaveNow);
        }
        void NewWorld(bool sandbox)
        {Try(()=>{SaveNow();Open(new LocalWorld{world=World.Create(string.IsNullOrWhiteSpace(newName)?"Nowe miasteczko":newName.Trim(),sandbox)});SaveNow();});}
        LocalWorld FindLocal(string id)
        {if(local!=null&&local.world.worldId==id)return local;return repo.ListIds().Contains(id)?repo.Load(id):null;}
        void Preserve(World world)
        {world.worldId=Guid.NewGuid().ToString();world.name="Kopia — "+world.name;if(world.name.Length>80)world.name=world.name.Substring(0,80);
         var copy=new LocalWorld{world=world};repo.Write(world.worldId,JsonUtility.ToJson(copy),++writeSequence);}
        void Duplicate()
        {Try(()=>{SaveNow();var w=local.world.Copy();w.worldId=Guid.NewGuid().ToString();w.name="Kopia — "+w.name;if(w.name.Length>80)w.name=w.name.Substring(0,80);Open(new LocalWorld{world=w});SaveNow();Note("Utworzono niezależną kopię świata.");});}
        public void SaveNow()
        {if(local==null)return;local.world=sim.State;WorldValidation.Validate(local.world);repo.Write(local.world.worldId,JsonUtility.ToJson(local),++writeSequence);saveTimer=0;}
        async void SaveLater()
        {
            if(local==null)return;local.world=sim.State;var json=JsonUtility.ToJson(local);var id=local.world.worldId;var sequence=++writeSequence;
            try{await Task.Run(()=>repo.Write(id,json,sequence));}catch(Exception){Note("Nie udało się zapisać na urządzeniu. Sprawdź wolne miejsce.",10);}
        }
        void Try(Action action){try{action();}catch(Exception e){Note(e.Message,9);}}
        async void Run(Func<Task> action)
        {if(Busy)return;uiBusy=true;try{await action();}catch(OperationCanceledException){Note("Operacja przerwana po zmianie sesji.");}catch(Exception e){Note(e.Message,9);}finally{uiBusy=false;}}
        void Update()
        {
            if(local==null)return;
            float delta=Mathf.Min(Time.unscaledDeltaTime,.2f);
            if(panel==Panel.None&&!local.world.paused)
            {
                accumulator+=delta*local.world.speed;
                while(accumulator>=.2f){accumulator-=.2f;if(sim.Step()){Mark();if(local.world.tick%50==0)view.Reconcile(local.world,sim);}}
                view.Animate(delta*local.world.speed);
            }
            else accumulator=0;
            saveTimer+=delta;syncTimer+=delta;thermalTimer+=delta;
            if(saveTimer>=30){saveTimer=0;SaveLater();}
            if(syncTimer>=120){syncTimer=0;if(session.SignedIn&&!Busy&&local.dirty&&string.IsNullOrEmpty(local.conflictRevisionId))Run(()=>cloud.Push());}
            if(thermalTimer>=5){AdjustQuality(thermalTimer);thermalTimer=0;}
            string imported=AppleServices.ImportedPath();if(imported!="")Try(()=>{if(Busy){Note("Zakończ synchronizację i ponownie wybierz plik.");return;}SaveNow();Open(repo.ImportAsCopy(imported));SaveNow();Note("Wczytano kopię świata.");});
            if(Application.isMobilePlatform&&Input.touchCount==1) {
                var touch=Input.GetTouch(0);
                if(touch.phase==TouchPhase.Began){uiTouchStart=touch.position;uiScrollDragged=false;}
                if(touch.phase==TouchPhase.Moved&&Vector2.Distance(uiTouchStart,touch.position)>8*Scale) {
                    float y=(Screen.safeArea.yMax-uiTouchStart.y)/Scale;
                    if(panel==Panel.None&&y>H-94){catalogScroll.x=Mathf.Max(0,catalogScroll.x-touch.deltaPosition.x/Scale);uiScrollDragged=true;}
                    else if(panel!=Panel.None&&Mathf.Abs(touch.deltaPosition.y)>Mathf.Abs(touch.deltaPosition.x)){
                        listScroll.y=Mathf.Max(0,listScroll.y+touch.deltaPosition.y/Scale);uiScrollDragged=true;
                    }
                }
            }
            InputUpdate();
        }
        void AdjustQuality(float elapsed)
        {
            bool constrained=eco||AppleServices.LowPower||AppleServices.Thermal>=2;
            healthySeconds=constrained?0:healthySeconds+elapsed;
            int level=constrained?0:healthySeconds>=20?1:qualityLevel;
            if(level==qualityLevel||level<0)return;qualityLevel=level;
            Application.targetFrameRate=level==0?30:60;QualitySettings.shadowDistance=level==0?12:45;
            if(pipeline){pipeline.renderScale=level==0?.75f:1;pipeline.shadowDistance=level==0?12:35;}
        }
        bool OverUi(Vector2 point)
        {
            float x=(point.x-Screen.safeArea.x)/Scale,y=(Screen.safeArea.yMax-point.y)/Scale;
            return panel!=Panel.None||x<0||x>W||y<0||y>H||y<68||y>H-94||x>W-260;
        }
        void InputUpdate()
        {
            if(panel!=Panel.None){held=false;view.Preview(0,0,false,false);return;}
            if(Input.touchCount>0)
            {
                fingerSequence=true;
                if(Input.touchCount>=2){var a=Input.GetTouch(0);var b=Input.GetTouch(1);float distance=Vector2.Distance(a.position,b.position);
                    if(lastPinch>0){view.Zoom*=lastPinch/Mathf.Max(1,distance);Vector2 mid=(a.position+b.position)*.5f;
                        if(view.Ground(lastPointer,out var old)&&view.Ground(mid,out var now))view.Target+=old-now;lastPointer=mid;}
                    else lastPointer=(a.position+b.position)*.5f;
                    lastPinch=distance;held=false;dragged=true;view.ApplyCamera();return;}
                var touch=Input.GetTouch(0);
                if(touch.phase==TouchPhase.Began){lastPinch=0;BeginPointer(touch.position);}
                else if(touch.phase==TouchPhase.Moved&&lastPinch==0)MovePointer(touch.position);
                else if(touch.phase==TouchPhase.Ended){if(lastPinch==0)EndPointer(touch.position);held=false;}
                Preview(touch.position);return;
            }
            if(fingerSequence){fingerSequence=false;lastPinch=0;held=false;return;}
            Vector2 mouse=Input.mousePosition;
            if(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1)||Input.GetMouseButtonDown(2))BeginPointer(mouse);
            if(held&&(Input.GetMouseButton(0)||Input.GetMouseButton(1)||Input.GetMouseButton(2)))MovePointer(mouse);
            if(Input.GetMouseButtonUp(0))EndPointer(mouse);
            if(Input.GetMouseButtonUp(1)||Input.GetMouseButtonUp(2))held=false;
            if(!OverUi(mouse)&&Mathf.Abs(Input.mouseScrollDelta.y)>.01f){view.Zoom*=Mathf.Pow(.9f,Input.mouseScrollDelta.y);view.ApplyCamera();}
            if(Input.GetKeyDown(KeyCode.R)){rotation=(rotation+1)%4;}
            if(Input.GetKey(KeyCode.Q))view.Yaw-=50*Time.unscaledDeltaTime;
            if(Input.GetKey(KeyCode.E))view.Yaw+=50*Time.unscaledDeltaTime;
            if(Input.GetKeyDown(KeyCode.Space)){local.world.paused=!local.world.paused;Mark();}
            if((Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.LeftCommand))&&Input.GetKeyDown(KeyCode.Z))Undo();
            view.ApplyCamera();Preview(mouse);
        }
        void BeginPointer(Vector2 p){if(OverUi(p)){held=false;return;}held=true;dragged=false;down=p;lastPointer=p;}
        void MovePointer(Vector2 p)
        {if(!held)return;if(Vector2.Distance(down,p)>12*Scale)dragged=true;
         if(dragged&&view.Ground(lastPointer,out var old)&&view.Ground(p,out var now)){view.Target+=old-now;view.ApplyCamera();}lastPointer=p;}
        void EndPointer(Vector2 p)
        {if(held&&!dragged&&!OverUi(p)&&view.Ground(p,out var hit))Act(Mathf.RoundToInt(hit.x),Mathf.RoundToInt(hit.z));held=false;}
        void Preview(Vector2 p)
        {
            if(OverUi(p)||selectedKind<0&&movingId==""||!view.Ground(p,out var hit)){view.Preview(0,0,false,false);return;}
            int x=Mathf.RoundToInt(hit.x),z=Mathf.RoundToInt(hit.z);bool valid=movingId!=""?sim.At(x,z)==null:sim.CanPlace(selectedKind,x,z)==null;
            view.Preview(x,z,valid,x>=0&&x<local.world.width&&z>=0&&z<local.world.height);
        }
        void Act(int x,int z)
        {
            if(x<0||x>=local.world.width||z<0||z>=local.world.height)return;
            string error=null;
            if(movingId!=""){error=sim.Move(movingId,x,z,rotation);if(error==null)movingId="";}
            else if(selectedKind>=0)error=sim.Place(selectedKind,x,z,rotation);
            else if(selectedKind==-2)error=sim.Remove(x,z);
            else{selectedId=sim.At(x,z)?.id??"";view.Select(sim.At(x,z));Feedback(true);return;}
            if(error!=null){Feedback(false);Note(error);return;}
            Mark();view.Reconcile(local.world,sim);view.Select(local.world.buildings.FirstOrDefault(b=>b.id==selectedId));Feedback(true);SaveLater();
        }
        void Undo(){if(sim.Undo()){Mark();view.Reconcile(local.world,sim);view.Select(null);Feedback(true);SaveLater();}else Note("Cofanie jest dostępne podczas edycji na pauzie.");}
        void OnApplicationPause(bool pause){if(pause){Try(SaveNow);AppleServices.Suspend();}else{accumulator=0;syncTimer=120;}}
        void OnApplicationFocus(bool focus){if(!focus)Try(SaveNow);else accumulator=0;}
        void OnApplicationQuit(){Try(SaveNow);}
        Texture2D Texture(Color color){var t=new Texture2D(1,1);t.SetPixel(0,0,color);t.Apply();textures.Add(t);return t;}
        void Styles()
        {
            if(label!=null)return;
            label=new GUIStyle(GUI.skin.label){fontSize=19,wordWrap=true};label.normal.textColor=new Color(.9f,.94f,.9f);
            small=new GUIStyle(label){fontSize=15};title=new GUIStyle(label){fontSize=26,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){fontSize=18,wordWrap=true,padding=new RectOffset(8,8,5,5)};
            button.normal.background=Texture(new Color(.17f,.25f,.25f));button.normal.textColor=Color.white;
            button.hover.background=Texture(new Color(.22f,.35f,.31f));button.active.background=Texture(new Color(.29f,.48f,.38f));
            activeButton=new GUIStyle(button);activeButton.normal.background=Texture(new Color(.32f,.49f,.36f));
            box=new GUIStyle(GUI.skin.box);box.normal.background=Texture(new Color(.075f,.13f,.14f,.96f));
            field=new GUIStyle(GUI.skin.textField){fontSize=20,padding=new RectOffset(10,10,10,10)};
        }
        bool Btn(Rect r,string text,bool selected=false)=>GUI.Button(r,text,selected?activeButton:button);
        bool Wide(string text)=>GUILayout.Button(text,button,GUILayout.Height(52));
        void OpenPanel(Panel next){panel=next;listScroll=Vector2.zero;if(next==Panel.Worlds){Try(SaveNow);RefreshLocal();}}
        void OnGUI()
        {
            if(local==null)return;Styles();
            if(uiScrollDragged&&Event.current.type==EventType.MouseUp){Event.current.Use();uiScrollDragged=false;}
            GUI.matrix=Matrix4x4.TRS(new Vector3(Screen.safeArea.x,Screen.height-Screen.safeArea.yMax,0),Quaternion.identity,new Vector3(Scale,Scale,1));
            GUI.Box(new Rect(0,0,W,64),GUIContent.none,box);GUI.Label(new Rect(16,5,W-520,29),local.world.name,title);
            string stats=(local.world.sandbox?"Swobodne budowanie":local.world.money+" zł")+"   ·   "+local.world.families.Count+" rodzin   ·   dzień "+(local.world.tick/300+1);
            GUI.Label(new Rect(16,35,W-520,25),stats,small);
            if(Btn(new Rect(W-495,7,90,50),local.world.paused?"Graj":"Pauza")){local.world.paused=!local.world.paused;selectedKind=-1;movingId="";Mark();}
            if(Btn(new Rect(W-399,7,62,50),local.world.speed+"×")){local.world.speed=local.world.speed==4?1:local.world.speed*2;Mark();}
            if(Btn(new Rect(W-331,7,100,50),"Światy"))OpenPanel(Panel.Worlds);
            if(Btn(new Rect(W-225,7,105,50),"Chmura"))OpenPanel(Panel.Cloud);
            if(Btn(new Rect(W-114,7,105,50),"Opcje"))OpenPanel(Panel.Settings);
            GUI.Box(new Rect(W-252,73,252,H-173),GUIContent.none,box);
            GUILayout.BeginArea(new Rect(W-238,85,224,H-196));
            var selected=local.world.buildings.FirstOrDefault(b=>b.id==selectedId);
            if(selected!=null){GUILayout.Label(Catalog.Get(selected.kind).Name,title);GUILayout.Label(sim.Connected(selected)?"Połączony z drogą wjazdową":"Brak połączenia z wjazdem po lewej",label);
                var family=local.world.families.FirstOrDefault(f=>f.homeId==selected.id);if(family!=null)GUILayout.Label(family.name+"\nZadowolenie: "+family.mood+"%",label);
                if(Wide("Przenieś / obróć")){movingId=selected.id;selectedKind=-1;local.world.paused=true;Mark();Note("Wybierz nowe pole. Obrót: przycisk ↻ lub R.");}}
            else{GUILayout.Label("Twoja okolica",title);GUILayout.Label(local.world.families.Count==0?"Zbuduj dom przy drodze i naciśnij Graj. Rodziny pojawią się po chwili.":
                "Zadowolenie: "+Mathf.RoundToInt((float)local.world.families.Average(f=>f.mood))+"%\nBilans: "+sim.LastBalance+" zł / okres",label);
                GUILayout.Label("Sklep, praca, park, kawiarnia i przychodnia blisko domów poprawiają nastrój.",small);}
            if(Wide("Cele i nagrody"))OpenPanel(Panel.Goals);
            if(Wide("Obrót ↻")){rotation=(rotation+1)%4;if(movingId=="")view.Yaw+=90;view.ApplyCamera();}
            GUILayout.EndArea();
            GUI.Box(new Rect(0,H-94,W,94),GUIContent.none,box);
            catalogScroll=GUI.BeginScrollView(new Rect(0,H-91,W,89),catalogScroll,new Rect(0,0,16*102,68));
            if(Btn(new Rect(5,3,96,63),"Poznaj",selectedKind==-1&&movingId=="")){selectedKind=-1;movingId="";}
            for(int i=0;i<Catalog.All.Length;i++)if(Btn(new Rect((i+1)*102+5,3,96,63),Catalog.All[i].Name+"\n"+Catalog.All[i].Cost+" zł",selectedKind==i))
                {selectedKind=i;movingId="";local.world.paused=true;Mark();}
            if(Btn(new Rect(13*102+5,3,96,63),"Usuń\nzwrot 50%",selectedKind==-2)){selectedKind=-2;movingId="";}
            if(Btn(new Rect(14*102+5,3,96,63),"Cofnij"))Undo();
            if(Btn(new Rect(15*102+5,3,96,63),"Obróć\n"+(rotation*90)+"°")){rotation=(rotation+1)%4;}
            GUI.EndScrollView();
            if(panel!=Panel.None)DrawPanel();
            if(Time.unscaledTime<toastUntil){GUI.Box(new Rect(20,H-147,Mathf.Max(220,W-290),47),GUIContent.none,box);GUI.Label(new Rect(30,H-142,Mathf.Max(200,W-310),42),toast,small);}
            GUI.matrix=Matrix4x4.identity;
        }
        void DrawPanel()
        {
            var rect=new Rect(Mathf.Max(10,W*.08f),14,W*.84f,H-28);GUI.Box(rect,GUIContent.none,box);GUI.BeginGroup(rect);
            GUI.Label(new Rect(20,10,rect.width-150,38),panel==Panel.Worlds?"Twoje światy":panel==Panel.Cloud?"Kontynuuj na drugim urządzeniu":panel==Panel.Goals?"Małe cele, wielkie miasto":panel==Panel.History?"Historia zapisów":"Po Twojemu",title);
            if(Btn(new Rect(rect.width-124,8,108,48),"Zamknij")){panel=Panel.None;GUI.EndGroup();return;}
            GUI.enabled=!Busy;
            GUILayout.BeginArea(new Rect(20,67,rect.width-40,rect.height-79));listScroll=GUILayout.BeginScrollView(listScroll);
            switch(panel)
            {
                case Panel.Worlds:
                    GUILayout.Label("Nowy świat",label);newName=GUILayout.TextField(newName,80,field,GUILayout.Height(50));
                    GUILayout.BeginHorizontal();if(Wide("Z ekonomią"))NewWorld(false);if(Wide("Swobodne budowanie"))NewWorld(true);if(Wide("Kopia obecnego"))Duplicate();GUILayout.EndHorizontal();
                    if(Wide("Importuj plik świata")){if(!AppleServices.PickWorld())Note("Import z pliku wymaga aplikacji na Macu lub iPhonie.");}
                    foreach(var saved in localList.ToArray())if(Wide(saved.world.name+"   ·   "+saved.world.families.Count+" rodzin")){Try(()=>{SaveNow();Open(repo.Load(saved.world.worldId));});}
                    break;
                case Panel.Cloud:
                    GUILayout.Label(cloud.Status,label);
                    if(!session.Configured){GUILayout.Label("Ten build działa lokalnie. Po wdrożeniu API i dodaniu konfiguracji GCP w buildzie pojawi się logowanie i synchronizacja.",label);break;}
                    if(!session.SignedIn){GUILayout.Label("Email konta testowego",small);email=GUILayout.TextField(email,120,field,GUILayout.Height(50));
                        password=GUILayout.PasswordField(password,'*',128,field,GUILayout.Height(50));
                        if(Wide("Zaloguj"))Run(async()=>{try{await session.Login(email,password);Note("Zalogowano.");}finally{password="";}});break;}
                    GUILayout.BeginHorizontal();if(Wide("Zapisz do chmury"))Run(()=>cloud.Push());
                    if(Wide("Wczytaj główny zapis"))Run(async()=>{SaveNow();var next=await cloud.Download(local.world.worldId);Open(next);SaveNow();});GUILayout.EndHorizontal();
                    if(!string.IsNullOrEmpty(local.conflictRevisionId)){
                        GUILayout.Label("Oba urządzenia zmieniły świat. Możesz kontynuować lokalną wersję, wczytać chmurę albo zachować oba światy.",label);
                        if(Wide("Kontynuuj tę wersję"))Run(()=>cloud.ContinueLocal());if(Wide("Zachowaj lokalną jako osobny świat"))Duplicate();}
                    GUILayout.BeginHorizontal();if(Wide("Lista światów w chmurze"))Run(async()=>{remoteList.Clear();var page=await cloud.List();remoteList.AddRange(page.worlds??Array.Empty<RemoteWorld>());cloudCursor=page.nextCursor;});
                    if(Wide("Historia obecnego świata"))Run(async()=>{history.Clear();var page=await cloud.History();history.AddRange(page.revisions??Array.Empty<RemoteRevision>());historyCursor=page.nextCursor;OpenPanel(Panel.History);});GUILayout.EndHorizontal();
                    foreach(var remote in remoteList.ToArray())if(string.IsNullOrEmpty(remote.deletedAt)&&Wide("Wczytaj: "+remote.name))Run(async()=>{SaveNow();var next=await cloud.Download(remote.worldId);Open(next);SaveNow();});
                    if(!string.IsNullOrEmpty(cloudCursor)&&Wide("Więcej światów"))Run(async()=>{var page=await cloud.List(cloudCursor);remoteList.AddRange(page.worlds??Array.Empty<RemoteWorld>());cloudCursor=page.nextCursor;});
                    if(Wide("Wyloguj")){session.Logout();remoteList.Clear();history.Clear();password="";Note("Wylogowano. Lokalne światy zachowane.");}
                    break;
                case Panel.History:
                    GUILayout.Label("Wybrana rewizja otworzy się jako nowy świat. Obecny zapis pozostanie zachowany.",small);
                    foreach(var rev in history.OrderByDescending(r=>r.createdAt).ToArray())if(Wide(rev.createdAt+" · dzień "+(rev.tick/300+1)+(rev.state=="conflict"?" · alternatywna wersja":"")))
                        Run(async()=>{SaveNow();var next=await cloud.Download(local.world.worldId,rev.revisionId);Open(next);SaveNow();});
                    if(!string.IsNullOrEmpty(historyCursor)&&Wide("Starsze / kolejne rewizje"))Run(async()=>{var page=await cloud.History(historyCursor);history.AddRange(page.revisions??Array.Empty<RemoteRevision>());historyCursor=page.nextCursor;});
                    break;
                case Panel.Goals:
                    foreach(var goal in Goals.All){bool claimed=local.world.claimedGoals.Contains(goal.Id),complete=goal.Complete(local.world);
                        GUILayout.Label(goal.Title+" · "+goal.Reward+" zł"+(claimed?" · odebrano":""),label);
                        if(!claimed&&complete&&Wide("Odbierz nagrodę")){if(sim.Claim(goal.Id)){Mark();Feedback(true,2);SaveLater();}}}
                    break;
                case Panel.Settings:
                    if(Wide("Haptyka: "+(AppleServices.Haptics?"włączona":"wyłączona"))){AppleServices.Haptics=!AppleServices.Haptics;PlayerPrefs.SetInt("haptics",AppleServices.Haptics?1:0);Feedback(true);}
                    GUILayout.Label("Siła haptyki",label);float intensity=GUILayout.HorizontalSlider(AppleServices.Intensity,0,1,GUILayout.Height(35));
                    if(Mathf.Abs(intensity-AppleServices.Intensity)>.001f){AppleServices.Intensity=intensity;PlayerPrefs.SetFloat("haptic-strength",intensity);}
                    if(Wide("Sprawdź haptykę"))Feedback(true,2);
                    GUILayout.Label("Dźwięki",label);float volume=GUILayout.HorizontalSlider(audioSource.volume,0,1,GUILayout.Height(35));
                    if(Mathf.Abs(volume-audioSource.volume)>.001f){audioSource.volume=volume;PlayerPrefs.SetFloat("sound",volume);}
                    if(Wide("Grafika: "+(eco?"oszczędzanie baterii":"automatyczna"))){eco=!eco;PlayerPrefs.SetInt("eco",eco?1:0);qualityLevel=-1;healthySeconds=20;AdjustQuality(5);}
                    if(Wide("Eksportuj świat"))Try(()=>{SaveNow();if(!AppleServices.Share(repo.FilePath(local.world.worldId)))Note("Plik świata zapisano w folderze danych aplikacji.");});
                    if(Wide("Zrób zdjęcie miasta"))Run(Photo);
                    GUILayout.Label("Przesuwanie: przeciągnij mapę. Zoom: dwa palce lub kółko. Mac: Q/E obrót, R obrót budynku, spacja pauza, ⌘Z cofanie.",small);
                    break;
            }
            GUILayout.EndScrollView();GUILayout.EndArea();GUI.enabled=true;
            if(Busy)GUI.Label(new Rect(22,rect.height-40,rect.width-50,30),"Pracuję… lokalny świat pozostaje zapisany.",small);
            GUI.EndGroup();
        }
        async Task Photo()
        {
            panel=Panel.None;await Task.Yield();
            // Render the camera only, without UI, at a bounded resolution.
            int width=1600,height=Mathf.RoundToInt(1600f*Screen.height/Screen.width);var target=new RenderTexture(width,height,24);
            var previous=RenderTexture.active;var oldTarget=view.Camera.targetTexture;Texture2D image=null;
            try{RenderPipeline.SubmitRenderRequest(view.Camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var path=Path.Combine(Application.temporaryCachePath,"Town-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".png");
                File.WriteAllBytes(path,image.EncodeToPNG());AppleServices.Share(path);Note("Zdjęcie miasta gotowe.");}
            finally{view.Camera.targetTexture=oldTarget;RenderTexture.active=previous;target.Release();Destroy(target);if(image)Destroy(image);}
        }
        void OnDestroy(){foreach(var texture in textures)Destroy(texture);if(pipeline)Destroy(pipeline);if(clickClip)Destroy(clickClip);if(goalClip)Destroy(goalClip);}
    }
}
