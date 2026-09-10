using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
static class Program
{
    static int Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);int count=0,errors=0;
        foreach(var path in Directory.GetFiles(Path.Combine(root,"apps/game/Assets"),"*.cs",SearchOption.AllDirectories))
        {
            foreach(var symbols in new[]{new[]{"UNITY_EDITOR","UNITY_IOS"},new[]{"UNITY_IOS"},new[]{"UNITY_STANDALONE_OSX"}})
            {
                var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path),new CSharpParseOptions(LanguageVersion.CSharp9,preprocessorSymbols:symbols),path);
                foreach(var error in tree.GetDiagnostics().Where(x=>x.Severity==DiagnosticSeverity.Error)){Console.Error.WriteLine(error);errors++;}
            }
            count++;
        }
        Console.WriteLine($"Parsed {count} Unity C# files in three platform configurations; {errors} syntax errors. This does not compile against Unity/Apple SDKs.");
        return errors==0?0:1;
    }
}
