using System;
using System.IO;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Snappy.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private FileDialogManager FileDialogManager;

    //ImGuiWindowFlags.NoResize
    public ConfigWindow(Plugin plugin) : base(
        "Snappy Settings",
         ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize)
    {
        this.Size = new Vector2(500, 115);
        this.SizeCondition = ImGuiCond.Always;

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
        // Add version label at the bottom
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text($"Snappy version {Configuration.Version}. Made with <3");
    }
}

