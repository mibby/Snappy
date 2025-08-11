using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using OtterGui.Raii;
using OtterGui.Text;
using Snappy.Utils;

namespace Snappy.UI;

public partial class MainWindow
{
    public const int GPoseObjectId = 201;
    public const int CharacterScreenIndex = 240;
    public const int ExamineScreenIndex = 241;
    public const int FittingRoomIndex = 242;
    public const int DyePreviewIndex = 243;

    private string _playerFilter = string.Empty;
    private string _playerFilterLower = string.Empty;
    private string _currentLabel = string.Empty;
    private ICharacter? _player;
    private int? _objIdxSelected;
    private bool _isActorSnapshottable;
    private bool _snapshotExistsForActor;
    private bool _isActorModifiable;


    private void ClearSelectedActorState()
    {
        _player = null;
        _currentLabel = string.Empty;
        _objIdxSelected = null;

        _isActorSnapshottable = false;
        _snapshotExistsForActor = false;
        _isActorModifiable = false;
    }

    private void UpdateSelectedActorState()
    {
        if (_player == null || _objIdxSelected == null)
        {
            ClearSelectedActorState();
            return;
        }

        // --- Update modifiable state ---
        var inGpose = Svc.Objects[201] != null;
        if (inGpose)
        {
            _isActorModifiable = true;
        }
        else
        {
            var isLocalPlayer = _player.ObjectIndex == Player.Object?.ObjectIndex;
            _isActorModifiable =
                isLocalPlayer
                && _plugin.Configuration.DisableAutomaticRevert
                && _plugin.Configuration.AllowOutsideGpose;
        }

        // --- Update snapshottable state ---
        _snapshotExistsForActor =
            _plugin.SnapshotManager.FindSnapshotPathForActor(_player) != null;

        if (inGpose)
        {
            _isActorSnapshottable = false;
        }
        else
        {
            var isSelf = string.Equals(
                _player.Name.TextValue,
                Player.Name,
                StringComparison.Ordinal
            );
            // This uses a 5-second cache internally.
            var isMarePaired = _plugin
                .IpcManager.GetMarePairedPlayers()
                .Any(p => p.Address == _player.Address);
            _isActorSnapshottable = isSelf || isMarePaired;
        }
    }

    private List<ICharacter> GetCurrentSortedActorList()
    {
        var uniqueActors = new Dictionary<IntPtr, ICharacter>();

        // Add self
        if (Player.Available)
        {
            uniqueActors[Player.Object.Address] = Player.Object;
        }

        // Add Mare players (uses 1-second cache internally)
        var marePlayers = _plugin.IpcManager.GetMarePairedPlayers();
        foreach (var marePlayer in marePlayers)
        {
            if (marePlayer.IsValid())
            {
                uniqueActors[marePlayer.Address] = marePlayer;
            }
        }

        var sortedList = uniqueActors.Values.ToList();

        // Local player always on top, the rest alphabetical.
        sortedList.Sort(
            (a, b) =>
            {
                var isALocalPlayer = Player.Available && a.Address == Player.Object.Address;
                var isBLocalPlayer = Player.Available && b.Address == Player.Object.Address;

                if (isALocalPlayer && !isBLocalPlayer)
                    return -1; // a (local player) comes first
                if (!isALocalPlayer && isBLocalPlayer)
                    return 1; // b (local player) comes first

                // Otherwise, sort alphabetically
                return string.Compare(
                    a.Name.ToString(),
                    b.Name.ToString(),
                    StringComparison.Ordinal
                );
            }
        );

        return sortedList;
    }

    private void DrawPlayerFilter()
    {
        const float buttonSize = 24f;
        const float spacing = 4f;          // consistent spacing between elements

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var inputWidth = availableWidth - buttonSize - spacing;

        // Input field
        ImGui.SetNextItemWidth(inputWidth);
        if (ImUtf8.InputText("##playerFilter", ref _playerFilter, "Filter Players..."))
            _playerFilterLower = _playerFilter.ToLowerInvariant();

        // Consistent spacing between input and button
        ImGui.SameLine(0, spacing);

        // Refresh button
        if (ImUtf8.IconButton(
                FontAwesomeIcon.Sync,
                tooltip: "Refresh Actor List",
                size: new Vector2(buttonSize, 0),
                disabled: false)
           )
        {
            // Force refresh the Mare cache and clear selection
            _plugin.IpcManager.RefreshMarePairedPlayers();
            ClearActorSelection();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh Actor List");
        }
    }

    private void DrawGPoseSelectable(ICharacter gposePlayer, int objIdx)
    {
        var playerName = gposePlayer.Name.ToString();
        if (!playerName.Any())
            return;

        DrawSelectable(gposePlayer, $"{playerName} (GPose)", objIdx);
    }

    private void DrawSelectable(ICharacter selectablePlayer, string label, int objIdx)
    {
        if (_playerFilterLower.Any() && !label.ToLowerInvariant().Contains(_playerFilterLower))
            return;

        var isSelected = _currentLabel == label;
        if (ImUtf8.Selectable(label, isSelected))
        {
            if (isSelected)
            {
                ClearSelectedActorState();
            }
            else
            {
                _currentLabel = label;
                _player = selectablePlayer;
                _objIdxSelected = objIdx;
                UpdateSelectedActorState();
            }
        }
    }

    private void DrawPlayerSelectable(ICharacter playerToDraw)
    {
        var playerName = playerToDraw.Name.ToString();
        if (!playerName.Any())
            return;

        var label = GetLabel(playerToDraw, playerName);
        DrawSelectable(playerToDraw, label, playerToDraw.ObjectIndex);
    }

    private static string GetLabel(ICharacter player, string playerName)
    {
        if (player.ObjectKind == ObjectKind.Player)
            return playerName;

        if (player.ModelType() == 0)
            return $"{playerName} (NPC)";

        return $"{playerName} (Monster)";
    }

    private (string Text, string Tooltip, bool IsDisabled) GetSnapshotButtonState()
    {
        if (_player == null)
        {
            return ("Save Snapshot", "Select an actor to save or update its snapshot.", true);
        }

        if (Svc.Objects[201] != null) // In Gpose
        {
            var buttonText = _snapshotExistsForActor ? "Update Snapshot" : "Save Snapshot";
            return (
                buttonText,
                "Saving or updating snapshots is unavailable while in GPose.",
                true
            );
        }

        if (!_isActorSnapshottable)
        {
            return (
                "Save Snapshot",
                "Can only save snapshots of yourself, or players you are paired with in Mare Synchronos.",
                true
            );
        }

        if (_snapshotExistsForActor)
        {
            return (
                "Update Snapshot",
                $"Update existing snapshot for {_player.Name.TextValue}.\n(Folder can be renamed freely)",
                false
            );
        }

        return ("Save Snapshot", $"Save a new snapshot for {_player.Name.TextValue}.", false);
    }

    private void DrawPlayerSelector()
    {
        ImGui.BeginGroup();
        DrawPlayerFilter();

        var buttonHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
        var listHeight = ImGui.GetContentRegionAvail().Y - buttonHeight;

        using (
            var child = ImRaii.Child(
                "ActorList",
                new Vector2(ImGui.GetContentRegionAvail().X, listHeight),
                false
            )
        )
        {
            if (child)
            {
                // Get real-time sorted actor list (Mare players + self)
                if (Svc.Objects[201] != null)
                {
                    // In GPose, show GPose actors + self
                    for (var i = GPoseObjectId; i < GPoseObjectId + 48; ++i)
                    {
                        var p = CharacterFactory.Convert(Svc.Objects[i]);
                        if (p == null)
                            continue;
                        DrawGPoseSelectable(p, i);
                    }
                }
                else
                {
                    // Outside GPose, show filtered Mare players + self
                    var sortedActors = GetCurrentSortedActorList();
                    foreach (var actor in sortedActors)
                    {
                        DrawPlayerSelectable(actor);
                    }
                }
            }
        }

        var (buttonText, tooltipText, isButtonDisabled) = GetSnapshotButtonState();

        if (
            ImUtf8.ButtonEx(
                buttonText,
                tooltipText,
                new Vector2(ImGui.GetContentRegionAvail().X, 0),
                isButtonDisabled
            )
        )
        {
            var updatedSnapshotPath = _plugin.SnapshotManager.UpdateSnapshot(_player!);
            if (updatedSnapshotPath != null)
            {
                _plugin.InvokeSnapshotsUpdated();
                if (
                    _selectedSnapshot != null
                    && string.Equals(
                        updatedSnapshotPath,
                        _selectedSnapshot.FullName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    LoadHistoryForSelectedSnapshot();
                }
            }
        }

        ImGui.EndGroup();
    }
}