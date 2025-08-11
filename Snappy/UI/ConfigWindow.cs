using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ECommons.Configuration;
using OtterGui.Raii;
using OtterGui.Text;

namespace Snappy.UI;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly FileDialogManager _fileDialogManager;
    private Plugin _plugin;

    public ConfigWindow(Plugin plugin)
        : base(
            $"Snappy Settings v{typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unknown"}",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(465, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _plugin = plugin;
        _configuration = plugin.Configuration;
        _fileDialogManager = plugin.FileDialogManager;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enableTheme = false; // Feature is disabled
        using (var d = ImRaii.Disabled(true))
        {
            ImUtf8.Checkbox("Enable Custom Theme"u8, ref enableTheme);
        }
        ImUtf8.HoverTooltip("This feature is currently under development and is disabled.");

        ImGui.Spacing();

        var disableRevert = _configuration.DisableAutomaticRevert;
        if (ImUtf8.Checkbox("Disable Automatic Revert on GPose Exit", ref disableRevert))
        {
            _configuration.DisableAutomaticRevert = disableRevert;
            EzConfig.Save();
        }
        ImUtf8.HoverTooltip(
            "Keeps snapshots applied on your character until you manually revert them or close the game.\nNormally, they revert when you leave GPose."
        );

        ImGui.Indent();
        using (var d = ImRaii.Disabled(!_configuration.DisableAutomaticRevert))
        {
            var allowOutside = _configuration.AllowOutsideGpose;
            if (
                ImUtf8.Checkbox(
                    "Allow loading to your character outside of GPose",
                    ref allowOutside
                )
            )
            {
                _configuration.AllowOutsideGpose = allowOutside;
                EzConfig.Save();
            }
        }
        ImGui.Unindent();

        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var iconColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImUtf8.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        }

        ImGui.SameLine();

        using (
            var textColor = ImRaii.PushColor(
                ImGuiCol.Text,
                ImGui.GetColorU32(ImGuiCol.TextDisabled)
            )
        )
        {
            ImUtf8.Text("Warning: These features are unsupported and may cause issues.");
        }

        ImGui.Separator();

        if (
            ImUtf8.Button(
                "Run Snapshot Migration Scan",
                new Vector2(ImGui.GetContentRegionAvail().X, 0)
            )
        )
        {
            _plugin.ManuallyRunMigration();
        }
        ImUtf8.HoverTooltip(
            "Manually scans your working directory for old-format snapshots and migrates them to the current format.\n"
            + "A backup is created before any changes are made."
        );

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Custom Penumbra Collection");
        ImGui.Text("Select a collection to merge with snapshots when applying to actors:");

        // Collection selector
        var customCollectionName = _configuration.CustomPenumbraCollectionName;
        if (ImGui.BeginCombo("Custom Collection", string.IsNullOrEmpty(customCollectionName) ? "None" : customCollectionName))
        {
            // Add "None" option
            if (ImGui.Selectable("None", string.IsNullOrEmpty(customCollectionName)))
            {
                _configuration.CustomPenumbraCollectionName = string.Empty;
                EzConfig.Save();
            }

            // Get all collections from Penumbra
            try
            {
                var collections = _plugin.IpcManager.GetCollections();
                // Sort collections alphabetically by name
                var sortedCollections = collections.OrderBy(c => c.Value).ToList();

                foreach (var collection in sortedCollections)
                {
                    var isSelected = customCollectionName == collection.Value;
                    if (ImGui.Selectable(collection.Value, isSelected))
                    {
                        _configuration.CustomPenumbraCollectionName = collection.Value;
                        EzConfig.Save();
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

        ImGui.Spacing();
    }
}