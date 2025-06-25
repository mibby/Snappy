using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.Logging;
using ECommons.Reflection;
using Snappy.IPC.Customize;
using Snappy.IPC.Glamourer;
using Snappy.IPC.Penumbra;
using Snappy.Models;
using Snappy.Utils;

namespace Snappy.IPC;

public class IpcManager : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly Plugin _plugin;

    public readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizeIpc _customize;

    public IpcManager(IDalamudPluginInterface pi, DalamudUtil dalamudUtil, Plugin plugin)
    {
        PluginLog.Debug("Creating IpcManager delegator");

        _dalamudUtil = dalamudUtil;
        _plugin = plugin;
        _penumbra = new PenumbraIpc();
        _glamourer = new GlamourerIpc();
        _customize = new CustomizeIpc();
    }

    public void Dispose()
    {
        _penumbra.Dispose();
        _glamourer.Dispose();
        _customize.Dispose();
    }

    // Penumbra passthroughs
    public void PenumbraRemoveTemporaryCollection(int objIdx) =>
        _penumbra.RemoveTemporaryCollection(objIdx);

    public void PenumbraRedraw(int objIdx) => _penumbra.Redraw(objIdx);

    public void PenumbraRedraw(IntPtr objPtr) => _penumbra.Redraw(objPtr);

    public string GetMetaManipulations(int objIdx) => _penumbra.GetMetaManipulations(objIdx);

    public Dictionary<string, HashSet<string>>? PenumbraGetGameObjectResourcePaths(int objIdx) =>
        _penumbra.GetGameObjectResourcePaths(objIdx);

    public void PenumbraSetTempMods(
        ICharacter character,
        int? idx,
        Dictionary<string, string> mods,
        string manips
    ) => _penumbra.SetTemporaryMods(character, idx, mods, manips);

    public string PenumbraResolvePath(string path) => _penumbra.ResolvePath(path);

    public string PenumbraResolvePathObject(string path, int objIdx) =>
        _penumbra.ResolvePathObject(path, objIdx);

    public string[] PenumbraReverseResolveObject(string path, int objIdx) =>
        _penumbra.ReverseResolveObject(path, objIdx);

    public string[] PenumbraReverseResolvePlayer(string path) =>
        _penumbra.ReverseResolvePlayer(path);
    public Dictionary<Guid, string> GetCollections() => _penumbra.GetCollections();
    public void MergeCollectionWithTemporary(int objIdx, string customCollectionName)
    {
        PluginLog.Debug($"[UI] MergeCollectionWithTemporary called with objIdx={objIdx}, collection='{customCollectionName}'");

        // This method will be called from the UI - we need to get the character and snapshot data
        var activeSnapshots = _plugin.SnapshotManager.ActiveSnapshots;
        PluginLog.Debug($"[UI] Found {activeSnapshots.Count} active snapshots");

        var snapshot = activeSnapshots.FirstOrDefault(s => s.ObjectIndex == objIdx);
        if (snapshot == null)
        {
            PluginLog.Debug($"[UI] No active snapshot found for object index {objIdx}");
            return;
        }

        // Get the character object from the object index
        var gameObject = Svc.Objects[objIdx];
        var character = CharacterFactory.Convert(gameObject);
        if (character == null)
        {
            PluginLog.Debug($"[UI] Could not convert game object at index {objIdx} to ICharacter");
            return;
        }

        PluginLog.Debug($"[UI] Found active snapshot for character '{character.Name.TextValue}'");

        // Try to load snapshot data from disk first (for saved snapshots)
        var charaName = character.Name.TextValue;
        var path = Path.Combine(_plugin.Configuration.WorkingDirectory, charaName);
        var snapshotJsonPath = Path.Combine(path, "snapshot.json");

        Dictionary<string, string> moddedPaths;
        string manipulationString;

        if (File.Exists(snapshotJsonPath))
        {
            PluginLog.Debug($"[UI] Loading snapshot data from disk at: {snapshotJsonPath}");
            var infoJson = File.ReadAllText(snapshotJsonPath);
            var snapshotInfo = JsonSerializer.Deserialize<SnapshotInfo>(infoJson);
            if (snapshotInfo == null)
            {
                PluginLog.Debug($"[UI] Failed to deserialize snapshot json for character {charaName}, aborting");
                return;
            }

            PluginLog.Debug($"[UI] Successfully loaded snapshot info with {snapshotInfo.FileReplacements.Count} file replacements");

            // Convert FileReplacements to the format expected by PenumbraIpc
            moddedPaths = new Dictionary<string, string>();
            foreach (var replacement in snapshotInfo.FileReplacements)
            {
                var gamePath = replacement.Key;
                var hash = replacement.Value;
                moddedPaths.Add(gamePath, Path.Combine(path, "_files", hash + ".dat"));
            }
            manipulationString = snapshotInfo.ManipulationString;
        }
        else
        {
            PluginLog.Debug($"[UI] No snapshot json found at {snapshotJsonPath}, this is a Brio actor with in-memory snapshot...");

            // For Brio actors, we need to get the ORIGINAL snapshot data, not the current mixed state
            // This ensures we always start from the clean original snapshot, not from any previous overrides
            moddedPaths = GetOriginalSnapshotDataForBrioActor(character, objIdx);
            manipulationString = GetMetaManipulations(objIdx);

            PluginLog.Debug($"[UI] Retrieved {moddedPaths.Count} mods from original Brio actor snapshot");
        }

        PluginLog.Debug("[UI] Calling PenumbraIpc.MergeCollectionWithTemporary...");

        // Call the PenumbraIpc method directly with the UI-selected collection name
        _penumbra.MergeCollectionWithTemporary(character, objIdx, customCollectionName,
            moddedPaths, manipulationString);

        PluginLog.Debug("[UI] MergeCollectionWithTemporary call completed");
    }

    /// <summary>
    /// Get the ORIGINAL snapshot data for a Brio actor by checking if we have stored snapshot data.
    /// If we have stored data from a previous collection override, use that. Otherwise, we need to
    /// capture the current snapshot data before applying any collection overrides.
    /// </summary>
    private Dictionary<string, string> GetOriginalSnapshotDataForBrioActor(ICharacter character, int objIdx)
    {
        PluginLog.Debug($"[UI] Getting ORIGINAL snapshot data for {character.Name.TextValue}");

        try
        {
            // Check if we already have stored original snapshot data for this actor
            var storedSnapshotData = _penumbra.GetStoredSnapshotData(objIdx);
            if (storedSnapshotData != null)
            {
                PluginLog.Debug($"[UI] Found stored original snapshot data with {storedSnapshotData.Count} mods");
                return storedSnapshotData;
            }

            // If no stored data, we need to capture the current snapshot data before applying any overrides
            // This should be the original snapshot that was applied to the Brio actor
            PluginLog.Debug("[UI] No stored data found, capturing current snapshot data as original");
            var originalFileReplacements = _plugin.SnapshotManager.GetFileReplacementsForCharacter(character);

            var moddedPaths = new Dictionary<string, string>();
            foreach (var replacement in originalFileReplacements)
            {
                foreach (var gamePath in replacement.GamePaths)
                {
                    moddedPaths[gamePath] = replacement.ResolvedPath;
                }
            }

            PluginLog.Debug($"[UI] Captured {moddedPaths.Count} original file replacements from Brio actor");
            return moddedPaths;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[UI] Error getting original Brio actor snapshot data: {ex.Message}");
            PluginLog.Debug("[UI] Using empty base - custom collection will provide all mods");
            return new Dictionary<string, string>();
        }
    }

    // Glamourer passthroughs
    public GlamourerIpc GlamourerIpc => _glamourer;

    public string GetGlamourerState(ICharacter c) => _glamourer.GetCharacterCustomization(c);

    public void ApplyGlamourerState(string? base64, ICharacter c) =>
        _glamourer.ApplyState(base64, c);

    public void UnlockGlamourerState(IGameObject c) => _glamourer.UnlockState(c);

    public void RevertGlamourerToAutomation(IGameObject c) => _glamourer.RevertToAutomation(c);

    // CustomizePlus passthroughs
    public bool IsCustomizePlusAvailable() => _customize.CheckApi();

    public string GetCustomizePlusScale(ICharacter c) => _customize.GetScaleFromCharacter(c);

    public Guid? SetCustomizePlusScale(IntPtr address, string scale) =>
        _customize.SetScale(address, scale);

    public void RevertCustomizePlusScale(Guid profileId) => _customize.Revert(profileId);

    // Mare passthroughs
    private List<ICharacter> _cachedMarePairedPlayers = new();
    private DateTime _lastMareCacheUpdateTime = DateTime.MinValue;
    private readonly TimeSpan _mareCacheDuration = TimeSpan.FromSeconds(5);

    private IDictionary? GetAllMareClientPairs()
    {
        if (!DalamudReflector.TryGetDalamudPlugin("MareSynchronos", out var marePlugin, true, true))
            return null;

        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host == null)
                return null;
            var serviceProvider = host.GetFoP("Services") as IServiceProvider;
            if (serviceProvider == null)
                return null;
            var pairManagerType = marePlugin
                .GetType()
                .Assembly.GetType("MareSynchronos.PlayerData.Pairs.PairManager");
            if (pairManagerType == null)
                return null;
            var pairManager = serviceProvider.GetService(pairManagerType);
            if (pairManager == null)
                return null;
            return pairManager.GetFoP("_allClientPairs") as IDictionary;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into Mare Synchronos to get client pairs.\n{e}"
            );
            return null;
        }
    }

    private object? GetMarePair(ICharacter character)
    {
        var allClientPairs = GetAllMareClientPairs();
        if (allClientPairs == null)
            return null;

        try
        {
            foreach (var pairObject in allClientPairs.Values)
            {
                var pairPlayerName = pairObject.GetFoP("PlayerName") as string;
                if (
                    string.Equals(
                        pairPlayerName,
                        character.Name.TextValue,
                        StringComparison.Ordinal
                    )
                )
                {
                    return pairObject;
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing Mare Synchronos pairs to find a specific pair.\n{e}"
            );
        }

        return null;
    }

    public List<ICharacter> GetMarePairedPlayers()
    {
        if (DateTime.UtcNow - _lastMareCacheUpdateTime < _mareCacheDuration)
        {
            _cachedMarePairedPlayers.RemoveAll(c => !c.IsValid());
            return _cachedMarePairedPlayers;
        }

        PluginLog.Debug("Refreshing Mare paired players cache.");
        _lastMareCacheUpdateTime = DateTime.UtcNow;

        var result = new List<ICharacter>();
        var allClientPairs = GetAllMareClientPairs();
        if (allClientPairs == null)
        {
            _cachedMarePairedPlayers = result;
            return result;
        }

        var playersInZone = Svc
            .Objects.OfType<IPlayerCharacter>()
            .Where(p => p.IsValid())
            .ToDictionary(p => p.Name.TextValue, p => (ICharacter)p, StringComparer.Ordinal);

        try
        {
            foreach (var pairObject in allClientPairs.Values)
            {
                var isVisible = pairObject.GetFoP("IsVisible") as bool? ?? false;
                var playerName = pairObject.GetFoP("PlayerName") as string;
                if (
                    isVisible
                    && !string.IsNullOrEmpty(playerName)
                    && playersInZone.TryGetValue(playerName, out var character)
                )
                {
                    result.Add(character);
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing Mare Synchronos pairs to get visible players.\n{e}"
            );
        }

        _cachedMarePairedPlayers = result;
        return result;
    }

    public object? GetCharacterDataFromMare(ICharacter character)
    {
        return _dalamudUtil
            .RunOnFrameworkThread(() =>
            {
                var pairObject = GetMarePair(character);
                if (pairObject == null)
                {
                    PluginLog.Debug(
                        $"No Mare pair found for character {character.Name.TextValue}."
                    );
                    return null;
                }
                return pairObject.GetFoP("LastReceivedCharacterData");
            })
            .Result;
    }

    public string? GetMareFileCachePath(string hash)
    {
        if (!DalamudReflector.TryGetDalamudPlugin("MareSynchronos", out var marePlugin, true, true))
            return null;

        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host == null)
                return null;
            var serviceProvider = host.GetFoP("Services") as IServiceProvider;
            if (serviceProvider == null)
                return null;
            var fileCacheManagerType = marePlugin
                .GetType()
                .Assembly.GetType("MareSynchronos.FileCache.FileCacheManager");
            if (fileCacheManagerType == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not find type MareSynchronos.FileCache.FileCacheManager."
                );
                return null;
            }
            var fileCacheManager = serviceProvider.GetService(fileCacheManagerType);
            if (fileCacheManager == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not get FileCacheManager service from IServiceProvider."
                );
                return null;
            }

            var getFileCacheMethod = fileCacheManagerType.GetMethod(
                "GetFileCacheByHash",
                new[] { typeof(string) }
            );
            if (getFileCacheMethod == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not find method GetFileCacheByHash in FileCacheManager."
                );
                return null;
            }

            var fileCacheEntityObject = getFileCacheMethod.Invoke(
                fileCacheManager,
                new object[] { hash }
            );
            if (fileCacheEntityObject == null)
                return null;

            return fileCacheEntityObject.GetFoP("ResolvedFilepath") as string;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into Mare Synchronos for file cache path.\n{e}"
            );
            return null;
        }
    }
}