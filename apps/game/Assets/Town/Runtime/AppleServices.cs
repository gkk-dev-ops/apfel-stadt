using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Town.Runtime
{
    public static class AppleServices
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string Library="__Internal";
#else
        const string Library="TownApple";
#endif
        [DllImport(Library)] static extern void TownHaptic(int kind,float strength);
        [DllImport(Library)] static extern int TownThermalState();
        [DllImport(Library)] static extern int TownLowPowerMode();
        [DllImport(Library)] static extern void TownStopHaptics();
        [DllImport(Library)] static extern int TownKeychainSet(string key,string value);
        [DllImport(Library)] static extern IntPtr TownKeychainGet(string key);
        [DllImport(Library)] static extern void TownKeychainDelete(string key);
        [DllImport(Library)] static extern void TownFree(IntPtr ptr);
        [DllImport(Library)] static extern void TownShareFile(string path);
        [DllImport(Library)] static extern void TownPickWorld();
        [DllImport(Library)] static extern IntPtr TownPollImportedPath();
        static bool unavailable;
        static float lastPulse;
        static readonly Dictionary<string,string> sessionOnly=new Dictionary<string,string>();
        public static bool Haptics=true;
        public static float Intensity=0.55f;
        public static bool NativeAvailable=>!unavailable&&(Application.platform==RuntimePlatform.IPhonePlayer||Application.platform==RuntimePlatform.OSXPlayer);
        static T Call<T>(Func<T> fn,T fallback)
        {
            if(!NativeAvailable)return fallback;
            try{return fn();}catch(DllNotFoundException){unavailable=true;}catch(EntryPointNotFoundException){unavailable=true;}
            return fallback;
        }
        static string StringResult(IntPtr ptr)
        {if(ptr==IntPtr.Zero)return "";try{return Marshal.PtrToStringUTF8(ptr)??"";}finally{TownFree(ptr);}}
        public static void Pulse(int kind=0)
        {if(!Haptics||Time.unscaledTime-lastPulse<0.1f)return;lastPulse=Time.unscaledTime;Call(()=>{TownHaptic(kind,Intensity);return true;},false);}
        public static int Thermal=>Call(()=>TownThermalState(),0);
        public static bool LowPower=>Call(()=>TownLowPowerMode()!=0,false);
        public static void Suspend()=>Call(()=>{TownStopHaptics();return true;},false);
        public static bool StoreSecret(string key,string value)
        {sessionOnly[key]=value;return Call(()=>TownKeychainSet(key,value)!=0,false);}
        public static string ReadSecret(string key)
        {var value=Call(()=>StringResult(TownKeychainGet(key)),"");return value!=""?value:sessionOnly.TryGetValue(key,out var memory)?memory:"";}
        public static void DeleteSecret(string key)
        {sessionOnly.Remove(key);Call(()=>{TownKeychainDelete(key);return true;},false);}
        public static bool Share(string path)=>Call(()=>{TownShareFile(path);return true;},false);
        public static bool PickWorld()=>Call(()=>{TownPickWorld();return true;},false);
        public static string ImportedPath()=>Call(()=>StringResult(TownPollImportedPath()),"");
    }
}
