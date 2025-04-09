using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Snappy.Managers.Customize;
using Snappy.Managers.Glamourer;
using Snappy.Managers.Penumbra;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Action = System.Action;

namespace Snappy.Managers;

public delegate void PenumbraRedrawEvent(IntPtr address, int objTblIdx);
public delegate void HeelsOffsetChange(float change);
public delegate void PenumbraResourceLoadEvent(IntPtr drawObject, string gamePath, string filePath);
public delegate void CustomizePlusScaleChange(string? scale);

public class IpcManager : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> actionQueue = new();

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizeIpc _customize;

    public IpcManager(IDalamudPluginInterface pi, DalamudUtil dalamudUtil)
    {
        Logger.Verbose("Creating IpcManager delegator");

        _dalamudUtil = dalamudUtil;
        _penumbra = new PenumbraIpc(pi, dalamudUtil, actionQueue);
        _glamourer = new GlamourerIpc(pi, dalamudUtil, actionQueue);
        _customize = new CustomizeIpc(pi, dalamudUtil, actionQueue);

        _dalamudUtil.FrameworkUpdate += HandleActionQueue;
        _dalamudUtil.ZoneSwitchEnd += () => actionQueue.Clear();
    }

    private void HandleActionQueue()
    {
        if (actionQueue.TryDequeue(out var action) && action != null)
        {
            Logger.Debug("Execution action in queue: " + action.Method);
            action();
        }
    }

    public void Dispose()
    {
        _penumbra.Dispose();
        _glamourer.Dispose();
        _customize.Dispose();
        _dalamudUtil.FrameworkUpdate -= HandleActionQueue;
        actionQueue.Clear();
    }

    // Penumbra passthroughs
    public void PenumbraRemoveTemporaryCollection(string name) => _penumbra.RemoveTemporaryCollection(name);
    public void PenumbraRedraw(int objIdx) => _penumbra.Redraw(objIdx);
    public void PenumbraRedraw(IntPtr objPtr) => _penumbra.Redraw(objPtr);
    public string GetMetaManipulations(int objIdx) => _penumbra.GetMetaManipulations(objIdx);
    public void PenumbraSetTempMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips) => _penumbra.SetTemporaryMods(character, idx, mods, manips);
    // Passthroughs for Penumbra path helpers
    public string PenumbraResolvePath(string path) => _penumbra.ResolvePath(path);
    public string PenumbraResolvePathObject(string path, int objIdx) => _penumbra.ResolvePathObject(path, objIdx);
    public string[] PenumbraReverseResolveObject(string path, int objIdx) => _penumbra.ReverseResolveObject(path, objIdx);
    public string[] PenumbraReverseResolvePlayer(string path) => _penumbra.ReverseResolvePlayer(path);


    // Glamourer passthroughs
    public GlamourerIpc GlamourerIpc => _glamourer;
    public string GetGlamourerState(ICharacter c) => _glamourer.GetCharacterCustomization(c.Address);
    public void ApplyGlamourerState(string? base64, ICharacter c) => _glamourer.ApplyState(base64, c);
    public void RevertGlamourerState(IGameObject c) => _glamourer.RevertState(c);

    // CustomizePlus passthroughs
    public bool IsCustomizePlusAvailable() => _customize.CheckApi();
    public string GetCustomizePlusScale(ICharacter c) => _customize.GetScaleFromCharacter(c);
    public void SetCustomizePlusScale(IntPtr address, string scale) => _customize.SetScale(address, scale);
    public void RevertCustomizePlusScale(IntPtr address) => _customize.Revert(address);
}
