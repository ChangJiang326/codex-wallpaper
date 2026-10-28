using System;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using CodexWallpaper;

class BackgroundTests {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
    static int Main(){
        string root=Path.Combine(Path.GetTempPath(),"CodexWallpaperBackground-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string registryPath=@"Software\CodexWallpaperTests\"+Guid.NewGuid().ToString("N");
        try {
            var gate=new AutoApplyGate();string first="window:10:old-process";string restarted="window:10:new-process";
            Check(gate.CanAttempt(first,0)&&gate.FirstAttempt(first),"new client gets automatic application and one animation");
            gate.Begin(first);gate.Success(first,0);
            Check(!gate.FirstAttempt(first),"same window does not replay animation on focus or minimize");
            gate.Suppress(first);
            Check(!gate.CanAttempt(first,1000),"manual restore is not immediately overridden");
            Check(gate.CanAttempt(restarted,1000)&&gate.FirstAttempt(restarted),"client restart gets saved wallpaper despite reused handle");
            gate.Failure(restarted,10);
            Check(!gate.CanAttempt(restarted,19)&&gate.CanAttempt(restarted,20),"engine failures back off for ten seconds");
            gate.Failure(restarted,20);gate.Failure(restarted,30);
            Check(!gate.CanAttempt(restarted,10000),"three failures stop repeated startup attempts");
            gate.Reset(restarted);Check(gate.CanAttempt(restarted,10000),"saving settings permits explicit retry");
            gate.Clear();Check(gate.CanAttempt(first,0),"re-enabling automatic mode clears previous suppression");
            var serializer=new JavaScriptSerializer();var migrated=serializer.Deserialize<Settings>("{\"LastProject\":\"a.json\"}");
            Check(migrated.AutoApply&&migrated.StartWithWindows,"existing profiles migrate to automatic startup defaults");
            var off=serializer.Deserialize<Settings>(serializer.Serialize(new Settings{AutoApply=false,StartWithWindows=false}));
            Check(!off.AutoApply&&!off.StartWithWindows,"explicit disabled preferences survive reload");
            string project=Path.Combine(root,"project.json");File.WriteAllText(project,"{\"title\":\"保存一次\",\"type\":\"scene\"}");
            var settings=new Settings{LastProject=project};Check(BackgroundWatch.SavedWallpaper(settings).Title=="保存一次","background resolves exact saved project without loading gallery");
            File.WriteAllText(project,"{");bool failed=false;try{BackgroundWatch.SavedWallpaper(settings);}catch{failed=true;}Check(failed,"corrupt saved project cannot silently switch to another wallpaper");
            settings.LastProject=Path.Combine(root,"missing.json");failed=false;try{BackgroundWatch.SavedWallpaper(settings);}catch(FileNotFoundException){failed=true;}Check(failed,"missing wallpaper requires new selection");
            string exe=Path.Combine(root,"中文 路径.exe");File.WriteAllText(exe,"fixture");
            using(var key=Registry.CurrentUser.CreateSubKey(registryPath)) {
                key.SetValue("UnrelatedApp","keep");LoginStartup.Configure(key,true,exe);
                Check((string)key.GetValue(LoginStartup.ValueName)=="\""+exe+"\" --background","login entry quotes unicode and spaces and requests silent mode");
                LoginStartup.Configure(key,false,exe);
                Check(key.GetValue(LoginStartup.ValueName)==null&&(string)key.GetValue("UnrelatedApp")=="keep","disable removes only this application's login entry");
            }
            string prefix=@"Local\CodexWallpaperTests-"+Guid.NewGuid()+"-";
            using(var signals=new InstanceSignals(prefix)) {
                InstanceSignals.Send("show",prefix);Check(signals.ShowRequested()&&!signals.ShowRequested(),"second launch wakes existing UI exactly once");
                InstanceSignals.Send("restore",prefix);Check(signals.RestoreRequested()&&!signals.ExitRequested(),"restore signal does not terminate resident process");
                InstanceSignals.Send("exit",prefix);Check(signals.ExitRequested(),"explicit exit reaches background instance");
            }
            Console.WriteLine("All "+checks+" background checks passed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Directory.Delete(root,true);Registry.CurrentUser.DeleteSubKeyTree(registryPath,false);}
    }
}
