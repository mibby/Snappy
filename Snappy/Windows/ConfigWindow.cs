using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;

namespace Snappy.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private FileDialogManager FileDialogManager;
    private Plugin Plugin;
    public string Version { get; set; } = string.Empty;

    public ConfigWindow(Plugin plugin) : base(
        "Snappy Settings",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(465, 250),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.Plugin = plugin;
        this.Configuration = plugin.Configuration;
        this.FileDialogManager = plugin.FileDialogManager;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var workingDirectory = Configuration.WorkingDirectory;
        ImGui.InputText("Export Folder", ref workingDirectory, 255, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        string folderIcon = FontAwesomeIcon.Folder.ToIconString();
        if (ImGui.Button(folderIcon))
        {
            FileDialogManager.OpenFolderDialog("Where do you want to save your snaps?", (status, path) =>
            {
                if (!status)
                {
                    return;
                }

                if (Directory.Exists(path))
                {
                    this.Configuration.WorkingDirectory = path;
                    this.Configuration.Save();
                }
            });
        }
        ImGui.PopFont();

        ImGui.Text("Glamourer design fallback string");
        string fallbackString = Configuration.FallBackGlamourerString;
        ImGui.InputText("##input-format", ref fallbackString, 2500);
        if (fallbackString != Configuration.FallBackGlamourerString)
        {
            Configuration.FallBackGlamourerString = fallbackString;
            Configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Custom Penumbra Collection");
        ImGui.Text("Select a collection to merge with snapshots when applying to actors:");

        // Collection selector
        var customCollectionName = Configuration.CustomPenumbraCollectionName;
        if (ImGui.BeginCombo("Custom Collection", string.IsNullOrEmpty(customCollectionName) ? "None" : customCollectionName))
        {
            // Add "None" option
            if (ImGui.Selectable("None", string.IsNullOrEmpty(customCollectionName)))
            {
                Configuration.CustomPenumbraCollectionName = string.Empty;
                Configuration.Save();
            }

            // Get all collections from Penumbra
            try
            {
                var collections = Plugin.IpcManager.GetCollections();
                foreach (var collection in collections)
                {
                    var isSelected = customCollectionName == collection.Value;
                    if (ImGui.Selectable(collection.Value, isSelected))
                    {
                        Configuration.CustomPenumbraCollectionName = collection.Value;
                        Configuration.Save();
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

        if (!string.IsNullOrEmpty(customCollectionName))
        {
            ImGui.Text("This collection's mods will override snapshot mods (higher priority).");
            ImGui.Text("Perfect for animation mods, poses, etc. that should apply on top.");
        }

        // Add version label at the bottom
        ImGui.Spacing();
        ImGui.Separator();

        // Display version text with red heart
        ImGui.Text($"Snappy version {typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unknown"}. Made with");
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.82f, 0.18f, 0.18f, 1.0f)); // Red color (similar to 0xFF3030D0)
        ImGui.Text(FontAwesomeIcon.Heart.ToIconString());
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }
}