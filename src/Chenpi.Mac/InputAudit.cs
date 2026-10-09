using Avalonia;
using System.Text.Json;

namespace Chenpi;

// Explicit command-line fixture only; uses isolated test saves. Input itself is
// delivered through the macOS window server, never by calling scene handlers.
internal sealed partial class PetWindow
{
    private void WriteInputAudit(string folder)
    {
        Directory.CreateDirectory(folder);
        string command=Path.Combine(folder,"command.json");
        if(File.Exists(command))
        {
            using var json=JsonDocument.Parse(File.ReadAllText(command));
            var root=json.RootElement;
            if(root.TryGetProperty("floating",out var floating))engine.State.Floating=floating.GetBoolean();
            if(root.TryGetProperty("scale",out var scale)){engine.State.Scale=scale.GetDouble();scene.LayoutWorld();}
            if(root.TryGetProperty("food",out var food))engine.State.Food=food.GetDouble();
            if(root.TryGetProperty("closeSettings",out _))settings?.Close();
            MacNative.Configure(this,engine.State.Floating,hidden||fullscreen);
            File.Delete(command);
        }
        var targets=scene.InputTargets.ToDictionary(pair=>pair.Key,pair=>
        {
            Point global=MacNative.EventPoint(this,pair.Value);
            return new{X=global.X,Y=global.Y,ClientX=pair.Value.X,ClientY=pair.Value.Y};
        });
        var value=new{Time=clock.Elapsed.TotalSeconds,Native=MacNative.InputState(this),Scene=scene.InputState,Targets=targets,SettingsOpen=settings is not null,RenderScaling,scene.IsVisible,engine.State.Floating};
        string path=Path.Combine(folder,"state.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value));File.Move(path+".tmp",path,true);
    }
}
