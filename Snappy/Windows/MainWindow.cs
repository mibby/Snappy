using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Snappy.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Snappy.Windows;

public partial class MainWindow : Window, IDisposable
{
    private const float SelectorWidth = 200;
    private string _selectedCollectionToMerge = string.Empty;
    private ICharacter? _selectedActorForMerge;
    private Plugin Plugin;

    // Reference to active snapshots from the SnapshotManager
    private IReadOnlyList<SnapshotManager.ActiveSnapshot> ActiveSnapshots => Plugin.SnapshotManager.ActiveSnapshots;

    public MainWindow(Plugin plugin) : base(
        "Snappy", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(595, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.Plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (ImGui.Button("Show Settings"))
        {
            this.Plugin.DrawConfigUI();
        }

        ImGui.SameLine();
        if (ImGui.Button("Revert snapshots"))
        {
            this.Plugin.SnapshotManager.RevertAllSnapshots();
        }

        ImGui.SameLine();
        if (ImGui.Button("Import MCDF file"))
        {
            Plugin.FileDialogManager.OpenFileDialog("Snapshot selection", ".mcdf", (status, path) =>
            {
                if (!status)
                {
                    return;
                }

                if (File.Exists(path[0]))
                {
                    this.Plugin.MCDFManager.LoadMareCharaFile(path[0]);
                    this.Plugin.MCDFManager.ExtractMareCharaFile();
                }
            }, 1, Plugin.Configuration.WorkingDirectory);
        }

        ImGui.SameLine();
        if (ImGui.Button("Export snapshot as PMP"))
        {
            Plugin.FileDialogManager.OpenFolderDialog("Snapshot selection", (status, path) =>
            {
                if (!status)
                {
                    return;
                }

                if (Directory.Exists(path))
                {
                    Plugin.PMPExportManager.SnapshotToPMP(path);
                }
            }, Plugin.Configuration.WorkingDirectory);
        }

        ImGui.Spacing();

        // Add the merge collection UI section
        DrawMergeCollectionSection();

        ImGui.Spacing();

        this.DrawPlayerSelector();
        if (!currentLabel.Any())
            return;

        ImGui.SameLine();
        this.DrawActorPanel();
    }

    private void DrawMergeCollectionSection()
    {
        if (!ActiveSnapshots.Any())
            return;

        ImGui.Separator();
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Override Snapshot with Collection");
        ImGui.Text("Apply collection mods on top of snapshot (collection has priority)");

        // Collection selector
        if (ImGui.BeginCombo("Collection to Merge", _selectedCollectionToMerge))
        {
            // Get all collections from Penumbra
            try
            {
                var collections = Plugin.IpcManager.GetCollections();

                foreach (var collection in collections)
                {
                    var isSelected = _selectedCollectionToMerge == collection.Value;
                    if (ImGui.Selectable(collection.Value, isSelected))
                    {
                        _selectedCollectionToMerge = collection.Value;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }
            catch (Exception ex)
            {
                ImGui.Text("Error loading collections: " + ex.Message);
            }

            ImGui.EndCombo();
        }

        // Actor selector
        if (ImGui.BeginCombo("Target Actor", _selectedActorForMerge?.Name.TextValue ?? "Select Actor"))
        {
            foreach (var snapshot in ActiveSnapshots)
            {
                var isSelected = _selectedActorForMerge == snapshot.Character;
                if (ImGui.Selectable(snapshot.Character.Name.TextValue, isSelected))
                {
                    _selectedActorForMerge = snapshot.Character;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (ImGui.Button("Apply Collection Override") &&
            !string.IsNullOrEmpty(_selectedCollectionToMerge) &&
            _selectedActorForMerge != null)
        {
            Plugin.IpcManager.MergeCollectionWithTemporary(
                _selectedActorForMerge.ObjectIndex,
                _selectedCollectionToMerge);

            // Refresh the actor to apply changes immediately
            Plugin.IpcManager._penumbra.Redraw(_selectedActorForMerge.ObjectIndex);

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Collection applied on top!");
        }

        // Add refresh button for collection changes
        ImGui.SameLine();
        if (ImGui.Button("Refresh Collections"))
        {
            Plugin.IpcManager._penumbra.RefreshAllMergedCollections();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh all active collection overrides when you've made changes to your Penumbra collections");
        }

        // Show status of active merged collections
        var activeCount = Plugin.IpcManager._penumbra.GetActiveMergedCollectionCount();
        if (activeCount > 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), $"Active collection overrides: {activeCount}");
        }
    }
}
