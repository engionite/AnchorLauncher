namespace AnchorLauncher.Models.Instances;

/// <summary>Transient runtime state — never persisted to instance.json.</summary>
public enum InstanceStatus
{
    Idle,
    Installing,
    Launching,
    Running
}
