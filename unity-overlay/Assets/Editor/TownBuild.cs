using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Town.Runtime;

namespace Town.Editor
{
    // Build entry points for the checked-in Unity 6.3 project.
    public static class TownBuild
    {
        [Serializable]
        private class Manifest
        {
            public string bundleId;
            public string unityVersion;
            public string configuration;
            public string target;
            public string createdAtUtc;
        }

        [Serializable]
        private class ClientConfig
        {
            public string apiBaseUrl;
            public string authApiKey;
            public string authProjectId;
        }

        [MenuItem("Town/Prepare playable project")]
        public static void PrepareGame()
        {
            Directory.CreateDirectory("Assets/Town/Generated");Directory.CreateDirectory("Assets/Scenes");
            const string root="Assets/Town/Generated/";
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(root+"TownPipeline.asset");
            if(!pipeline) {
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer,root+"TownRenderer.asset");
                pipeline=UniversalRenderPipelineAsset.Create(renderer);
                pipeline.msaaSampleCount=2;pipeline.renderScale=1;pipeline.shadowDistance=35;
                pipeline.supportsHDR=false;
                AssetDatabase.CreateAsset(pipeline,root+"TownPipeline.asset");
            }
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            QualitySettings.vSyncCount=0;QualitySettings.shadows=UnityEngine.ShadowQuality.HardOnly;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait=false;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
            PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.runInBackground=false;
            PlayerSettings.companyName="Our Town";PlayerSettings.productName="Town";
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=settings.FindProperty("activeInputHandler");if(input!=null){input.intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();}
            ConfigurePlugins();
            const string scenePath="Assets/Scenes/Town.unity";
            if(!File.Exists(scenePath)) {
                var lit=Shader.Find("Universal Render Pipeline/Lit");var unlit=Shader.Find("Universal Render Pipeline/Unlit");
                if(!lit||!unlit)throw new BuildFailedException("URP shaders missing. Finish package import first.");
                var material=AssetDatabase.LoadAssetAtPath<Material>(root+"World.mat");
                if(!material){material=new Material(lit){enableInstancing=true};AssetDatabase.CreateAsset(material,root+"World.mat");}
                var grid=AssetDatabase.LoadAssetAtPath<Material>(root+"Grid.mat");
                if(!grid){grid=new Material(unlit){color=new Color(.51f,.61f,.43f)};AssetDatabase.CreateAsset(grid,root+"Grid.mat");}
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var app=new GameObject("Nasze Miasteczko",typeof(TownApp)).GetComponent<TownApp>();
                app.worldMaterial=material;app.gridMaterial=grid;
                EditorSceneManager.SaveScene(scene,scenePath);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scenePath,true)};
            AssetDatabase.SaveAssets();Debug.Log("Playable Town scene prepared. Open Assets/Scenes/Town.unity and press Play.");
        }
        static void ConfigurePlugins()
        {
            string native="Assets/Town/Plugins/iOS/TownApple.mm";
            var ios=AssetImporter.GetAtPath(native) as PluginImporter;
            if(ios){ios.SetCompatibleWithAnyPlatform(false);ios.SetCompatibleWithEditor(false);ios.SetCompatibleWithPlatform(BuildTarget.iOS,true);ios.SaveAndReimport();}
            const string dylib="Assets/Town/Plugins/macOS/libTownApple.dylib";
            if(File.Exists(dylib)) {
                AssetDatabase.ImportAsset(dylib,ImportAssetOptions.ForceSynchronousImport);
                var mac=AssetImporter.GetAtPath(dylib) as PluginImporter;
                if(mac){mac.SetCompatibleWithAnyPlatform(false);mac.SetCompatibleWithEditor(false);mac.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX,true);mac.SetPlatformData(BuildTarget.StandaloneOSX,"CPU","ARM64");mac.SaveAndReimport();}
            }
        }

        public static void MacOS()
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            Build(BuildTarget.StandaloneOSX, NamedBuildTarget.Standalone, "Town.app");
        }

        public static void IOS()
        {
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetOSVersionString = "17.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            var team = Environment.GetEnvironmentVariable("APPLE_TEAM_ID");
            if (!string.IsNullOrEmpty(team) && !team.StartsWith("REPLACE"))
                PlayerSettings.iOS.appleDeveloperTeamID = team;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            Build(BuildTarget.iOS, NamedBuildTarget.iOS, "Xcode");
        }

        private static void Build(BuildTarget target, NamedBuildTarget namedTarget, string child)
        {
            VerifyEditorVersion();
            PrepareGame();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroupFor(target), target))
                throw new BuildFailedException("Required Unity platform build module is missing.");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || scenes.Any(s => !File.Exists(s)))
                throw new BuildFailedException("Enable valid game scenes in Build Settings (or run smoke first).");
            var bundle = Environment.GetEnvironmentVariable("BUNDLE_ID");
            if (string.IsNullOrWhiteSpace(bundle))
                throw new BuildFailedException("Set BUNDLE_ID explicitly.");
            var config = Environment.GetEnvironmentVariable("BUILD_CONFIGURATION") ?? "development";
            if (config != "development" && config != "release")
                throw new BuildFailedException("BUILD_CONFIGURATION must be development or release.");
            var outputRoot = Environment.GetEnvironmentVariable("TOWN_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new BuildFailedException("Use scripts/unity.sh to provide an isolated output directory.");
            var output = Path.GetFullPath(Path.Combine(outputRoot, child));
            PlayerSettings.productName = "Town";
            PlayerSettings.SetApplicationIdentifier(namedTarget, bundle);
            PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Metal });
            EditorUserBuildSettings.development = config == "development";
            // Intentionally no AutoRunPlayer, auto-connect profiler or script debugging.
            var options = config == "development" ? BuildOptions.Development : BuildOptions.None;
            const string configPath = "Assets/Resources/TownClientConfig.json";
            if (File.Exists(configPath))
                throw new BuildFailedException("Reserved generated path already exists: " + configPath);
            Directory.CreateDirectory("Assets/Resources");
            var client = new ClientConfig {
                apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "",
                authApiKey = Environment.GetEnvironmentVariable("AUTH_API_KEY") ?? "",
                authProjectId = Environment.GetEnvironmentVariable("AUTH_PROJECT_ID") ?? ""
            };
            if (client.apiBaseUrl.Length > 0 && !client.apiBaseUrl.StartsWith("https://", StringComparison.Ordinal))
                throw new BuildFailedException("Configured API_BASE_URL must use HTTPS.");
            try
            {
                File.WriteAllText(configPath, JsonUtility.ToJson(client, true));
                AssetDatabase.ImportAsset(configPath, ImportAssetOptions.ForceSynchronousImport);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = scenes,
                    locationPathName = output,
                    target = target,
                    options = options
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Build failed: {report.summary.result}, errors={report.summary.totalErrors}");
                var manifestDir = target == BuildTarget.iOS ? output : outputRoot;
                File.WriteAllText(Path.Combine(manifestDir, "town-build.json"), JsonUtility.ToJson(new Manifest {
                    bundleId = bundle,
                    unityVersion = Application.unityVersion,
                    configuration = config,
                    target = target.ToString(),
                    createdAtUtc = DateTime.UtcNow.ToString("O")
                }, true));
            }
            finally
            {
                AssetDatabase.DeleteAsset(configPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static BuildTargetGroup BuildTargetGroupFor(BuildTarget target) =>
            target == BuildTarget.iOS ? BuildTargetGroup.iOS : BuildTargetGroup.Standalone;

        private static void VerifyEditorVersion()
        {
            var expected = Environment.GetEnvironmentVariable("TOWN_EXPECTED_UNITY_VERSION");
            if (string.IsNullOrEmpty(expected) || expected != Application.unityVersion)
                throw new BuildFailedException("Installed Editor differs from the project version captured before launch. Restore any automatic upgrade and use the correct Editor.");
        }

        public static void CreateSmokeScene() => PrepareGame();
    }
}
