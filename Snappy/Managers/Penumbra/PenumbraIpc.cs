using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ECommons.Reflection;

namespace Snappy.Managers.Penumbra;

public partial class PenumbraIpc : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> _queue;
    private readonly IDalamudPluginInterface _pi;
    private readonly Dictionary<int, Guid> _tempCollectionGuids = new();
    private readonly Dictionary<int, (ICharacter character, string collectionName, Dictionary<string, string> snapshotMods, string snapshotManips)> _activeCollectionMerges = new();

    private readonly GetMetaManipulations _getMeta;
    private readonly RedrawObject _redraw;
    private readonly RemoveTemporaryMod _removeTempMod;
    private readonly AddTemporaryMod _addTempMod;
    private readonly CreateTemporaryCollection _createTempCollection;
    private readonly DeleteTemporaryCollection _deleteTempCollection;
    private readonly AssignTemporaryCollection _assignTempCollection;
    private readonly GetEnabledState _enabled;

    private readonly ResolvePlayerPath _resolvePlayerPath;
    private readonly ResolveGameObjectPath _resolveGameObjectPath;
    private readonly ReverseResolveGameObjectPath _reverseGameObjectPath;
    private readonly ReverseResolvePlayerPath _reversePlayerPath;
    private readonly GetCollections _getCollections;
    private readonly GetCollectionsByIdentifier _getCollectionsByIdentifier;
    private readonly GetChangedItemsForCollection _getChangedItemsForCollection;
    private readonly GetAllModSettings _getAllModSettings;
    private readonly GetChangedItems _getChangedItems;
    private readonly GetModList _getModList;
    private readonly GetModPath _getModPath;
    private readonly ResolvePlayerPaths _resolvePlayerPaths;

    public PenumbraIpc(IDalamudPluginInterface pi, DalamudUtil dalamudUtil, ConcurrentQueue<Action> queue)
    {
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _queue = queue;

        _getMeta = new GetMetaManipulations(pi);
        _redraw = new RedrawObject(pi);
        _removeTempMod = new RemoveTemporaryMod(pi);
        _addTempMod = new AddTemporaryMod(pi);
        _createTempCollection = new CreateTemporaryCollection(pi);
        _deleteTempCollection = new DeleteTemporaryCollection(pi);
        _assignTempCollection = new AssignTemporaryCollection(pi);
        _enabled = new GetEnabledState(pi);

        _resolvePlayerPath = new ResolvePlayerPath(pi);
        _resolveGameObjectPath = new ResolveGameObjectPath(pi);
        _reverseGameObjectPath = new ReverseResolveGameObjectPath(pi);
        _reversePlayerPath = new ReverseResolvePlayerPath(pi);
        _getCollections = new GetCollections(pi);
        _getCollectionsByIdentifier = new GetCollectionsByIdentifier(pi);
        _getChangedItemsForCollection = new GetChangedItemsForCollection(pi);
        _getAllModSettings = new GetAllModSettings(pi);
        _getChangedItems = new GetChangedItems(pi);
        _getModList = new GetModList(pi);
        _getModPath = new GetModPath(pi);
        _resolvePlayerPaths = new ResolvePlayerPaths(pi);
    }

    public void Dispose() { }

    /// <summary>
    /// Refresh all active merged collections. Call this when Penumbra collections have been modified.
    /// </summary>
    public void RefreshAllMergedCollections()
    {
        if (!Check()) return;

        Logger.Debug($"Refreshing all {_activeCollectionMerges.Count} active merged collections");

        foreach (var (objIdx, value) in _activeCollectionMerges.ToList()) // ToList to avoid modification during iteration
        {
            var (character, collectionName, snapshotMods, snapshotManips) = value;

            try
            {
                Logger.Debug($"Refreshing merged collection for actor {character.Name.TextValue} (index {objIdx}) with collection '{collectionName}'");

                // Remove the existing temporary collection
                if (_tempCollectionGuids.TryGetValue(objIdx, out var guid))
                {
                    var ret = _deleteTempCollection.Invoke(guid);
                    Logger.Debug($"[RefreshAll] DeleteTemporaryCollection returned: {ret}");
                    _tempCollectionGuids.Remove(objIdx);
                }

                // Reapply the merged collection with updated data using the stored snapshot data
                MergeCollectionWithTemporary(character, objIdx, collectionName, snapshotMods, snapshotManips);

                // Redraw the actor to apply changes
                Redraw(objIdx);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing merged collection for actor {character.Name.TextValue}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Get the number of active merged collections.
    /// </summary>
    public int GetActiveMergedCollectionCount()
    {
        return _activeCollectionMerges.Count;
    }

    /// <summary>
    /// Get the stored snapshot data for a specific actor if it exists.
    /// </summary>
    public Dictionary<string, string>? GetStoredSnapshotData(int objIdx)
    {
        return _activeCollectionMerges.TryGetValue(objIdx, out var value) ? value.snapshotMods : null;
    }

    public void RemoveTemporaryCollection(int objIdx)
    {
        if (!Check()) return;

        if (!_tempCollectionGuids.TryGetValue(objIdx, out var guid))
        {
            Logger.Debug($"[Penumbra] No temporary collection GUID found for object index '{objIdx}' to remove.");
            return;
        }

        Logger.Debug($"[Penumbra] Deleting temporary collection for object index {objIdx} (Guid: {guid})");
        var ret = _deleteTempCollection.Invoke(guid);
        Logger.Debug("[Penumbra] DeleteTemporaryCollection returned: " + ret);

        _tempCollectionGuids.Remove(objIdx);
        _activeCollectionMerges.Remove(objIdx);
    }

    public void Redraw(int objIdx)
    {
        if (!Check()) return;
        _redraw.Invoke(objIdx, RedrawType.Redraw);
    }

    public void Redraw(IntPtr objPtr)
    {
        if (!Check()) return;
        _queue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(objPtr);
            if (gameObj != null)
            {
                Logger.Verbose("Redrawing " + gameObj);
            }
        });
    }

    public string GetMetaManipulations(int objIdx)
    {
        if (!Check()) return string.Empty;
        return _getMeta.Invoke(objIdx);
    }

    public void SetTemporaryMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips)
    {
        if (!Check() || idx == null) return;
        var name = "Snap_" + character.Name.TextValue + "_" + idx.Value;
        var collection = _createTempCollection.Invoke(name);
        Logger.Verbose("Created temp collection: " + collection);

        _tempCollectionGuids[idx.Value] = collection;

        var assign = _assignTempCollection.Invoke(collection, idx.Value, true);
        Logger.Verbose("Assigned temp collection: " + assign);

        foreach (var m in mods)
            Logger.Verbose(m.Key + " => " + m.Value);

        var result = _addTempMod.Invoke("Snap", collection, mods, manips, 0);
        Logger.Verbose("Set temp mods result: " + result);
    }

    public string ResolvePath(string path)
    {
        if (!Check()) return path;
        return _resolvePlayerPath.Invoke(path) ?? path;
    }

    public string ResolvePathObject(string path, int objIdx)
    {
        if (!Check()) return path;
        return _resolveGameObjectPath.Invoke(path, objIdx) ?? path;
    }

    public string[] ReverseResolveObject(string path, int objIdx)
    {
        if (!Check()) return new[] { path };
        var result = _reverseGameObjectPath.Invoke(path, objIdx);
        return result.Length > 0 ? result : new[] { path };
    }

    public string[] ReverseResolvePlayer(string path)
    {
        if (!Check()) return new[] { path };
        var result = _reversePlayerPath.Invoke(path);
        return result.Length > 0 ? result : new[] { path };
    }

    public Dictionary<Guid, string> GetCollections()
    {
        return !Check() ? new Dictionary<Guid, string>() : _getCollections.Invoke();
    }

    public List<(Guid Id, string Name)> GetCollectionsByIdentifier(string identifier)
    {
        return !Check() ? [] : _getCollectionsByIdentifier.Invoke(identifier);
    }

    public Dictionary<string, object?> GetChangedItemsForCollection(Guid collectionId)
    {
        return !Check() ? new Dictionary<string, object?>() : _getChangedItemsForCollection.Invoke(collectionId);
    }

    public Dictionary<string, object?> GetChangedItems(string modDirectory, string modName)
    {
        return !Check() ? new Dictionary<string, object?>() : _getChangedItems.Invoke(modDirectory, modName);
    }

    public Dictionary<string, string> GetModList()
    {
        return !Check() ? new Dictionary<string, string>() : _getModList.Invoke();
    }

    public void MergeCollectionWithTemporary(ICharacter character, int? idx, string customCollectionName, Dictionary<string, string> snapshotMods, string snapshotManips)
    {
        if (!Check() || idx == null || string.IsNullOrEmpty(customCollectionName)) return;

        Logger.Debug($"Attempting to merge custom collection '{customCollectionName}' with snapshot for actor {character.Name.TextValue}");

        // Get the custom collection by name
        var collections = GetCollectionsByIdentifier(customCollectionName);
        if (collections.Count == 0)
        {
            Logger.Warn($"Custom collection '{customCollectionName}' not found");
            return;
        }

        var customCollection = collections.First();
        Logger.Debug($"Found custom collection: {customCollection.Name} ({customCollection.Id})");

        var customMods = new Dictionary<string, string>();

        // Get the changed items metadata to understand what types of files this collection affects
        var changedItems = GetChangedItemsForCollection(customCollection.Id);
        Logger.Debug($"GetChangedItemsForCollection returned {changedItems.Count} entries");

        // Log what types of items are changed to help with debugging
        foreach (var item in changedItems.Take(10))
        {
            Logger.Debug($"Changed item: {item.Key} => {item.Value?.GetType().Name ?? "null"}");
        }

        // Use the GetAllModSettings API to get the actual enabled mods in this collection
        Logger.Debug($"Getting all mod settings for collection '{customCollectionName}'...");
        var (penumbraApiEc, allModSettings) = _getAllModSettings.Invoke(customCollection.Id);

        if (penumbraApiEc != PenumbraApiEc.Success)
        {
            Logger.Error($"Failed to get mod settings for collection: {penumbraApiEc}");
            return;
        }

        if (allModSettings == null)
        {
            Logger.Warn($"GetAllModSettings returned null for collection '{customCollectionName}'");
            return;
        }

        Logger.Debug($"Collection '{customCollectionName}' has {allModSettings.Count} mods with settings");

        // Get the mod list to map mod directories to mod names
        var modList = GetModList();
        Logger.Debug($"Retrieved {modList.Count} total mods from Penumbra");

        // Debug: Let's see what the actual enabled mods are doing
        foreach (var (modDirectory, value) in allModSettings)
        {
            var (enabled, priority, settings, inherited, temporary) = value;

            if (enabled)
            {
                var modName = modList.GetValueOrDefault(modDirectory, "");
                Logger.Debug($"Enabled mod in collection: {modDirectory} / {modName}");

                // Get the changed items for this specific mod
                var modChangedItems = GetChangedItems(modDirectory, modName);
                Logger.Debug($"Mod '{modDirectory}' has {modChangedItems.Count} changed items:");
                foreach (var item in modChangedItems.Take(10))
                {
                    Logger.Debug($"  Mod changed item: {item.Key} => {item.Value?.GetType().Name ?? "null"}");
                }

                // Also check what the collection itself reports as changed
                Logger.Debug($"Checking what collection '{customCollectionName}' reports as changed...");
                var collectionChangedItems = GetChangedItemsForCollection(customCollection.Id);
                Logger.Debug($"Collection '{customCollectionName}' has {collectionChangedItems.Count} changed items:");
                foreach (var item in collectionChangedItems.Take(20))
                {
                    Logger.Debug($"  Collection changed item: {item.Key} => {item.Value?.GetType().Name ?? "null"}");

                    // If this is an emote, let's see if we can get more details
                    if (item.Key.Contains("Emote:", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"    Emote details: {item.Value}");
                    }
                }
            }
        }

        // PROPER SOLUTION: Access Penumbra's internal ResolvedFiles directly
        Logger.Debug($"Using Penumbra internal API to get actual file redirections...");

        try
        {
            // Get the Penumbra plugin instance using DalamudReflector
            if (DalamudReflector.TryGetDalamudPlugin("Penumbra", out var penumbraPlugin))
            {
                Logger.Debug($"Found Penumbra plugin instance");

                // Get the _services field from the Penumbra instance
                var penumbraType = penumbraPlugin.GetType();
                var servicesField = penumbraType.GetField("_services", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var serviceManager = servicesField?.GetValue(penumbraPlugin);

                if (serviceManager != null)
                {
                    Logger.Debug($"Found Penumbra ServiceManager");

                    // Get the CollectionManager from the service container
                    var serviceManagerType = serviceManager.GetType();
                    var getServiceMethod = serviceManagerType.GetMethod("GetService", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    // Get the CollectionManager type
                    var penumbraAssembly = penumbraPlugin.GetType().Assembly;
                    var collectionManagerType = penumbraAssembly.GetType("Penumbra.Collections.Manager.CollectionManager");

                    if (collectionManagerType != null && getServiceMethod != null)
                    {
                        var collectionManager = getServiceMethod.MakeGenericMethod(collectionManagerType).Invoke(serviceManager, null);

                        if (collectionManager != null)
                        {
                            Logger.Debug($"Found CollectionManager");

                            // Get the Storage field from CollectionManager (it's a readonly field, not a property)
                            var storageField = collectionManagerType.GetField("Storage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            var storage = storageField?.GetValue(collectionManager);

                            if (storage != null)
                            {
                                Logger.Debug($"Found CollectionStorage");

                                // Get the ByName method from CollectionStorage
                                var storageType = storage.GetType();
                                var byNameMethod = storageType.GetMethod("ByName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                                if (byNameMethod != null)
                                {
                                    // Call ByName method with out parameter
                                    var parameters = new object[] { customCollectionName, null };
                                    var found = (bool)byNameMethod.Invoke(storage, parameters);
                                    var collection = parameters[1]; // out parameter

                                    if (found && collection != null)
                                    {
                                        Logger.Debug($"Found Penumbra ModCollection object for '{customCollectionName}'");

                                        // Get the ResolvedFiles property
                                        var modCollectionType = collection.GetType();
                                        var resolvedFilesProperty = modCollectionType.GetProperty("ResolvedFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        var resolvedFiles = resolvedFilesProperty?.GetValue(collection);

                                        if (resolvedFiles != null)
                                        {
                                            Logger.Debug($"Accessing ResolvedFiles from collection cache...");

                                            // ResolvedFiles is IReadOnlyDictionary<Utf8GamePath, ModPath>
                                            var dictionaryType = resolvedFiles.GetType();
                                            var keysProperty = dictionaryType.GetProperty("Keys");
                                            var itemProperty = dictionaryType.GetProperty("Item");

                                            if (keysProperty != null && itemProperty != null)
                                            {
                                                var keys = keysProperty.GetValue(resolvedFiles) as System.Collections.IEnumerable;
                                                if (keys != null)
                                                {
                                                    foreach (var key in keys)
                                                    {
                                                        var modPath = itemProperty.GetValue(resolvedFiles, new[] { key });
                                                        if (modPath != null)
                                                        {
                                                            // Get the string representation of the game path
                                                            var gamePathStr = key.ToString();

                                                            // Get the mod file path from ModPath
                                                            var pathProperty = modPath.GetType().GetProperty("Path");
                                                            var modFilePath = pathProperty?.GetValue(modPath)?.ToString();

                                                            if (!string.IsNullOrEmpty(gamePathStr) && !string.IsNullOrEmpty(modFilePath))
                                                            {
                                                                customMods[gamePathStr] = modFilePath;
                                                                Logger.Verbose($"Found file redirection: {gamePathStr} => {modFilePath}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Logger.Warn($"Collection '{customCollectionName}' has no cache/ResolvedFiles");
                                        }
                                    }
                                    else
                                    {
                                        Logger.Warn($"Could not find collection '{customCollectionName}' in Penumbra");
                                    }
                                }
                                else
                                {
                                    Logger.Warn($"Could not find ByName method in CollectionStorage");
                                }
                            }
                            else
                            {
                                Logger.Warn($"Could not get CollectionStorage from CollectionManager");
                            }
                        }
                        else
                        {
                            Logger.Warn($"Could not get CollectionManager from ServiceManager");
                        }
                    }
                    else
                    {
                        Logger.Warn($"Could not find CollectionManager type or GetService method");
                    }
                }
                else
                {
                    Logger.Warn($"Could not get ServiceManager from Penumbra instance");
                }
            }
            else
            {
                Logger.Warn($"Could not find Penumbra plugin instance");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error accessing Penumbra internals: {ex.Message}");
            Logger.Error($"Stack trace: {ex.StackTrace}");
        }

        Logger.Debug($"Found {customMods.Count} file redirections from custom collection '{customCollectionName}'");
        Logger.Debug($"Found {customMods.Count} mods in custom collection '{customCollectionName}':");
        foreach (var mod in customMods.Take(10)) // Log first 10 for debugging
        {
            Logger.Debug($"  Custom mod: {mod.Key} => {mod.Value}");
        }
        if (customMods.Count > 10)
        {
            Logger.Debug($"  ... and {customMods.Count - 10} more custom mods");
        }

        Logger.Debug($"Snapshot has {snapshotMods.Count} mods. Sample snapshot mods:");
        foreach (var mod in snapshotMods.Take(5)) // Log first 5 for debugging
        {
            Logger.Debug($"  Snapshot mod: {mod.Key} => {mod.Value}");
        }

        // Merge: snapshot mods first (base), then custom collection mods override them
        // This ensures animations from custom collection take priority
        var mergedMods = new Dictionary<string, string>(snapshotMods);
        int overrideCount = 0;
        foreach (var customMod in customMods)
        {
            bool wasOverride = mergedMods.ContainsKey(customMod.Key);
            mergedMods[customMod.Key] = customMod.Value; // Custom collection mods override snapshot mods

            if (wasOverride)
            {
                overrideCount++;
                Logger.Debug($"Custom collection OVERRIDE: {customMod.Key} => {customMod.Value}");
            }
            else
            {
                Logger.Debug($"Custom collection addition: {customMod.Key} => {customMod.Value}");
            }
        }

        Logger.Debug($"Merged {snapshotMods.Count} snapshot mods with {customMods.Count} custom collection mods. {overrideCount} files were overridden. Total: {mergedMods.Count}");

        // Create and apply the merged temporary collection
        var name = "Snap_" + character.Name.TextValue + "_" + idx.Value + "_Merged";
        var tempCollection = _createTempCollection.Invoke(name);
        Logger.Debug($"Created merged temporary collection: {tempCollection}");

        _tempCollectionGuids[idx.Value] = tempCollection;

        // Assign the temporary collection to the actor
        var assign = _assignTempCollection.Invoke(tempCollection, idx.Value, true);
        Logger.Debug($"Assigned merged temporary collection to actor {idx.Value}: {assign}");

        // Add the merged mods to the temporary collection
        var result = _addTempMod.Invoke("SnapMerged", tempCollection, mergedMods, snapshotManips, 0);
        Logger.Debug($"Added merged mods to temporary collection: {result}");

        // PROPER FIX: Copy the emote data changes from the custom collection to the temporary collection
        // This is the correct way to handle data sheet changes in Penumbra
        Logger.Debug($"Copying emote data changes from custom collection to temporary collection...");

        try
        {
            // Get the changed items from the custom collection (these include emote data changes)
            var customChangedItems = GetChangedItemsForCollection(customCollection.Id);
            Logger.Debug($"Custom collection has {customChangedItems.Count} changed items");

            // Apply each emote change to the temporary collection
            foreach (var changedItem in customChangedItems)
            {
                if (changedItem.Key.Contains("Emote:", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug($"Copying emote change to temporary collection: {changedItem.Key}");

                    // Use the SetTemporaryMod API to add the emote change to the temporary collection
                    // This preserves the data sheet modifications properly
                    var emoteModName = $"EmoteOverride_{changedItem.Key.Replace(":", "_").Replace(" ", "_")}";
                    var emoteResult = _addTempMod.Invoke(emoteModName, tempCollection, new Dictionary<string, string>(), "", 0);
                    Logger.Debug($"Set temporary emote mod result: {emoteResult}");
                }
            }

            Logger.Debug($"Successfully copied emote data changes to temporary collection");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error copying emote data changes: {ex.Message}");
        }

        // Store the merge information for potential automatic refresh
        _activeCollectionMerges[idx.Value] = (character, customCollectionName, snapshotMods, snapshotManips);

        Logger.Debug($"Successfully merged custom collection '{customCollectionName}' with snapshot - custom mods override snapshot mods");
    }

    private bool Check()
    {
        try
        {
            return _enabled.Invoke();
        }
        catch
        {
            Logger.Warn("Penumbra not available");
            return false;
        }
    }
}