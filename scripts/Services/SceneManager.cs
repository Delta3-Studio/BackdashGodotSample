using SpaceWar.Models;

public partial class SceneManager : Node
{
    [Export] Godot.Collections.Dictionary<SceneName, PackedScene> knownScenes;
    [Export] Godot.Collections.Dictionary<SceneName, string> knownSceneNames;

    public static SceneManager Instance { get; private set; }

    public SceneManager()
    {
        if (IsInstanceValid(Instance) && !Instance.IsQueuedForDeletion() && Instance != this)
            Instance.QueueFree();

        Instance = this;
    }

    public override void _Ready()
    {
        knownScenes ??= [];
        knownSceneNames ??= [];
    }

    public void ChangeTo(SceneName name)
    {
        if (knownScenes.TryGetValue(name, out var scene))
        {
            ChangeTo(scene);
            return;
        }

        var fallback =
            knownSceneNames.TryGetValue(name, out var sceneName) && !string.IsNullOrWhiteSpace(sceneName)
                ? sceneName
                : Enum.GetName(name) ?? name.ToString();

        ChangeTo(fallback.ToLowerInvariant());
    }

    public void ChangeTo(PackedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Instance = null;
        GetTree().ChangeSceneToPacked(scene);
    }

    public void ChangeTo(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Instance = null;
        GetTree().ChangeSceneToFile($"res://scenes/{name}.tscn");
    }
}
