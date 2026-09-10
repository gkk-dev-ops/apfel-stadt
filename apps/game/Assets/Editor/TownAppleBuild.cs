#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
namespace Town.Editor
{
    public static class TownAppleBuild
    {
        [PostProcessBuild(100)]
        public static void Postprocess(BuildTarget target,string output)
        {
            if(target!=BuildTarget.iOS)return;
            var path=PBXProject.GetPBXProjectPath(output);var project=new PBXProject();project.ReadFromFile(path);
            var framework=project.GetUnityFrameworkTargetGuid();
            foreach(var name in new[]{"Foundation.framework","UIKit.framework","Security.framework","CoreHaptics.framework","UniformTypeIdentifiers.framework"})
                project.AddFrameworkToProject(framework,name,false);
            const string source="Libraries/Town/Plugins/iOS/TownApple.mm";
            var file=project.FindFileGuidByProjectPath(source);
            if(string.IsNullOrEmpty(file)) {
                // Unity's exported plugin path can vary. Resolve the actual project-relative path.
                foreach(var candidate in Directory.GetFiles(output,"TownApple.mm",SearchOption.AllDirectories)) {
                    var relative=candidate.Substring(output.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\','/');
                    file=project.FindFileGuidByProjectPath(relative);if(!string.IsNullOrEmpty(file))break;
                }
            }
            if(string.IsNullOrEmpty(file))throw new UnityEditor.Build.BuildFailedException("Native TownApple.mm is missing from Xcode export.");
            var flags=project.GetCompileFlagsForFile(framework,file)??new System.Collections.Generic.List<string>();
            if(!flags.Contains("-fobjc-arc"))flags.Add("-fobjc-arc");project.SetCompileFlagsForFile(framework,file,flags);
            project.WriteToFile(path);
            var plistPath=Path.Combine(output,"Info.plist");var plist=new PlistDocument();plist.ReadFromFile(plistPath);
            plist.root.SetString("CFBundleDisplayName","Nasze Miasteczko");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
