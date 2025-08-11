using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using System.IO;
using System.Numerics;

namespace Snappy.Windows
{
    public partial class MainWindow
    {
        private const uint RedHeaderColor = 0xFF1818C0;
        private const uint GreenHeaderColor = 0xFF18C018;
        public bool IsInGpose { get; private set; } = false;

        private void DrawPlayerHeader()
        {
            var color = player == null ? RedHeaderColor : GreenHeaderColor;
            var buttonColor = ImGui.GetColorU32(ImGuiCol.FrameBg);
            ImGui.Button($"{currentLabel}##playerHeader", -Vector2.UnitX * 0.0001f);
        }

        private void DrawPlayerPanel()
        {
            ImGui.Text("Save snapshot of player ");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            try
            {
                string saveIcon = FontAwesomeIcon.Save.ToIconString();
                if (ImGui.Button(saveIcon + "##SaveSnapshot"))
                {
                    //save snapshot
                    if (player != null)
                        Plugin.SnapshotManager.SaveSnapshot(player);
                }
            }
            finally
            {
                ImGui.PopFont();
            }
            if (!IsInGpose)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.Text("For best results, leave GPose first. Saving snapshots while\nGPose is active may result in broken/incorrect snapshots.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            ImGui.Text("Append to existing snapshot");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            try
            {
                string addIcon = FontAwesomeIcon.Plus.ToIconString();
                if (ImGui.Button(addIcon + "##AppendSnapshot"))
                {
                    if (player != null)
                        Plugin.SnapshotManager.AppendSnapshot(player);
                }
            }
            finally
            {
                ImGui.PopFont();
            }

            if (this.modifiable)
            {
                ImGui.Text("Load snapshot onto ");
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                try
                {
                    string loadIcon = FontAwesomeIcon.FolderOpen.ToIconString();
                    if (ImGui.Button(loadIcon + "##LoadSnapshot"))
                    {
                        Plugin.FileDialogManager.OpenFolderDialog("Snapshot selection", (status, path) =>
                        {
                            if (!status)
                            {
                                return;
                            }

                            if (Directory.Exists(path))
                            {
                                if (player != null && objIdxSelected.HasValue)
                                    Plugin.SnapshotManager.LoadSnapshot(player, objIdxSelected.Value, path);
                            }
                        }, Plugin.Configuration.WorkingDirectory);
                    }
                }
                finally
                {
                    ImGui.PopFont();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.Text("Loading snapshots can only be done on GPose actors.");
                ImGui.PopStyleColor();
            }
        }

        private void DrawMonsterPanel()
        {

        }

        private void DrawActorPanel()
        {
            using var raii = ImGuiRaii.NewGroup();
            DrawPlayerHeader();
            if (!ImGui.BeginChild("##playerData", -Vector2.One, true))
            {
                ImGui.EndChild();
                return;
            }

            if (player != null || player.ModelType() == 0)
                DrawPlayerPanel();
            else
                DrawMonsterPanel();

            ImGui.EndChild();
        }
    }
}