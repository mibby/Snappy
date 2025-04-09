// CustomizeIpc.cs
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace Snappy.Managers.Customize;

public partial class CustomizeIpc : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> _queue;

    private readonly ICallGateSubscriber<string> _apiVersion;
    private readonly ICallGateSubscriber<string> _branch;
    private readonly ICallGateSubscriber<string, string> _getTemp;
    private readonly ICallGateSubscriber<ICharacter?, string> _getFromChar;
    private readonly ICallGateSubscriber<string, ICharacter?, object> _setToChar;
    private readonly ICallGateSubscriber<ICharacter?, object> _revert;
    private readonly ICallGateSubscriber<string?, object> _onScaleUpdate;

    public CustomizeIpc(IDalamudPluginInterface pi, DalamudUtil dalamudUtil, ConcurrentQueue<Action> queue)
    {
        _dalamudUtil = dalamudUtil;
        _queue = queue;

        _apiVersion = pi.GetIpcSubscriber<string>("CustomizePlus.GetApiVersion");
        _branch = pi.GetIpcSubscriber<string>("CustomizePlus.GetBranch");
        _getTemp = pi.GetIpcSubscriber<string, string>("CustomizePlus.GetTemporaryScale");
        _getFromChar = pi.GetIpcSubscriber<ICharacter?, string>("CustomizePlus.GetBodyScaleFromCharacter");
        _setToChar = pi.GetIpcSubscriber<string, ICharacter?, object>("CustomizePlus.SetBodyScaleToCharacter");
        _revert = pi.GetIpcSubscriber<ICharacter?, object>("CustomizePlus.RevertCharacter");
        _onScaleUpdate = pi.GetIpcSubscriber<string?, object>("CustomizePlus.OnScaleUpdate");
    }

    public void Dispose() { }

    public bool CheckApi()
    {
        try
        {
            return _apiVersion.InvokeFunc() == "1.0" && _branch.InvokeFunc() == "eqbot";
        }
        catch
        {
            return false;
        }
    }

    public string GetScaleFromCharacter(ICharacter c)
    {
        if (!CheckApi()) return string.Empty;
        var scale = _getFromChar.InvokeFunc(c);
        return string.IsNullOrEmpty(scale) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public void SetScale(IntPtr address, string scale)
    {
        if (!CheckApi() || string.IsNullOrEmpty(scale)) return;
        _queue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(address);
            if (gameObj is ICharacter c)
            {
                Logger.Verbose("C+ apply for: " + c.Address.ToString("X"));
                _setToChar.InvokeAction(scale, c);
            }
        });
    }

    public void Revert(IntPtr address)
    {
        if (!CheckApi()) return;
        _queue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(address);
            if (gameObj is ICharacter c)
            {
                Logger.Verbose("C+ revert for: " + c.Address.ToString("X"));
                _revert.InvokeAction(c);
            }
        });
    }
}
