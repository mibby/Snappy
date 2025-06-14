using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using ImGuiNET;
using Newtonsoft.Json;
using Penumbra.String;
using Snappy.Interop;
using Snappy.Models;
using Snappy.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Snappy.Managers
{
    public class SnapshotManager
    {
        private Plugin Plugin;
        private List<ICharacter> tempCollections = new();

        public SnapshotManager(Plugin plugin)
        {
            this.Plugin = plugin;
        }

        public void RevertAllSnapshots()
        {
            foreach (var character in tempCollections)
            {
                Plugin.IpcManager.PenumbraRemoveTemporaryCollection(character.Name.TextValue);
                Plugin.IpcManager.RevertGlamourerState(character);
                Plugin.IpcManager.RevertCustomizePlusScale(character.Address);
            }
            tempCollections.Clear();
        }
        public bool AppendSnapshot(ICharacter character)
        {
            var charaName = character.Name.TextValue;
            var path = Path.Combine(Plugin.Configuration.WorkingDirectory, charaName);
            string infoJson = File.ReadAllText(Path.Combine(path, "snapshot.json"));
            if (infoJson == null)
            {
                Logger.Warn("No snapshot json found, aborting");
                return false;
            }
            SnapshotInfo? snapshotInfo = JsonSerializer.Deserialize<SnapshotInfo>(infoJson);
            if (snapshotInfo == null)
            {
                Logger.Warn("Failed to deserialize snapshot json, aborting");
                return false;
            }

            if (!Directory.Exists(path))
            {
                //no existing snapshot for character, just use save mode
                return this.SaveSnapshot(character);
            }

            //Merge file replacements
            List<FileReplacement> replacements = GetFileReplacementsForCharacter(character);

            Logger.Debug($"Got {replacements.Count} replacements");

            foreach (var replacement in replacements)
            {
                FileInfo replacementFile = new FileInfo(replacement.ResolvedPath);
                FileInfo fileToCreate = new FileInfo(Path.Combine(path, replacement.GamePaths[0]));
                if (!fileToCreate.Exists)
                {
                    //totally new file
                    fileToCreate.Directory.Create();
                    replacementFile.CopyTo(fileToCreate.FullName);
                    foreach (var gamePath in replacement.GamePaths)
                    {
                        var collisions = snapshotInfo.FileReplacements.Where(src => src.Value.Any(p => p == gamePath)).ToList();
                        //gamepath already exists in snapshot, overwrite with new file
                        foreach (var collision in collisions)
                        {
                            collision.Value.Remove(gamePath);
                            if (collision.Value.Count == 0)
                            {
                                //delete file if it no longer has any references
                                snapshotInfo.FileReplacements.Remove(collision.Key);
                                File.Delete(Path.Combine(path, collision.Key));
                            }
                        }
                    }
                    snapshotInfo.FileReplacements.Add(replacement.GamePaths[0], replacement.GamePaths);
                }
            }

            //Merge meta manips
            snapshotInfo.ManipulationString = Plugin.IpcManager.GetMetaManipulations(character.ObjectIndex);

            // Merge Customize+
            if (Plugin.IpcManager.IsCustomizePlusAvailable())
            {
                Logger.Debug("C+ api loaded, updating C+ data in append mode");
                var data = Plugin.IpcManager.GetCustomizePlusScale(character);
                if (data.IsNullOrEmpty())
                {
                    Logger.Debug("C+ data from IPC is empty, attempting to get from Mare Synchronos.");
                    data = Plugin.IpcManager.GetCustomizePlusScaleFromMare(character);
                    if (!data.IsNullOrEmpty())
                    {
                        Logger.Info("Successfully used C+ data from Mare Synchronos for append.");
                    }
                    else
                    {
                        Logger.Warn("C+ data from Mare Synchronos is also empty. C+ data will not be updated.");
                    }
                }
                if (!data.IsNullOrEmpty())
                {
                    File.WriteAllText(Path.Combine(path, "customizePlus.json"), data);
                }
            }

            // Save the glamourer string
            var glamourerString = Plugin.IpcManager.GetGlamourerState(character);
            if (string.IsNullOrEmpty(glamourerString))
            {
                Logger.Debug("Glamourer data from IPC is empty, attempting to get from Mare Synchronos.");
                glamourerString = Plugin.IpcManager.GetGlamourerStateFromMare(character);
                if (!string.IsNullOrEmpty(glamourerString))
                {
                    Logger.Info("Successfully used Glamourer data from Mare Synchronos for append.");
                }
                else
                {
                    Logger.Warn("Glamourer data from Mare Synchronos is also empty. Glamourer data will not be updated.");
                }
            }
            if (!string.IsNullOrEmpty(glamourerString))
            {
                snapshotInfo.GlamourerString = glamourerString;
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string infoJsonWrite = JsonSerializer.Serialize(snapshotInfo, options);
            File.WriteAllText(Path.Combine(path, "snapshot.json"), infoJsonWrite);

            return true;
        }
        public bool SaveSnapshot(ICharacter character)
        {
            var charaName = character.Name.TextValue;
            var path = Path.Combine(Plugin.Configuration.WorkingDirectory, charaName);
            SnapshotInfo snapshotInfo = new();

            if (Directory.Exists(path))
            {
                Logger.Warn("Snapshot already existed. Running in append mode.");
                return AppendSnapshot(character);
            }
            Directory.CreateDirectory(path);

            var glamourerString = Plugin.IpcManager.GetGlamourerState(character);
            if (string.IsNullOrEmpty(glamourerString))
            {
                Logger.Debug("Glamourer data from IPC is empty, attempting to get from Mare Synchronos.");
                glamourerString = Plugin.IpcManager.GetGlamourerStateFromMare(character);
                if (!string.IsNullOrEmpty(glamourerString))
                {
                    Logger.Info("Successfully used Glamourer data from Mare Synchronos.");
                }
                else
                {
                    Logger.Warn("Glamourer data from Mare Synchronos is also empty. Glamourer data will be empty in snapshot.");
                }
            }
            snapshotInfo.GlamourerString = glamourerString;
            Logger.Debug($"Got glamourer string: {snapshotInfo.GlamourerString}");

            List<FileReplacement> replacements = GetFileReplacementsForCharacter(character);

            foreach (var replacement in replacements)
            {
                Logger.Debug(replacement.GamePaths[0]);
            }

            Logger.Debug($"Got {replacements.Count} replacements");

            foreach (var replacement in replacements)
            {
                FileInfo replacementFile = new FileInfo(replacement.ResolvedPath);
                FileInfo fileToCreate = new FileInfo(Path.Combine(path, replacement.GamePaths[0]));
                fileToCreate.Directory.Create();
                replacementFile.CopyTo(fileToCreate.FullName);
                snapshotInfo.FileReplacements.Add(replacement.GamePaths[0], replacement.GamePaths);
            }

            snapshotInfo.ManipulationString = Plugin.IpcManager.GetMetaManipulations(character.ObjectIndex);

            if (Plugin.IpcManager.IsCustomizePlusAvailable())
            {
                Logger.Debug("C+ api loaded");
                var data = Plugin.IpcManager.GetCustomizePlusScale(character);

                if (data.IsNullOrEmpty())
                {
                    Logger.Debug("C+ data from IPC is empty, attempting to get from Mare Synchronos.");
                    data = Plugin.IpcManager.GetCustomizePlusScaleFromMare(character);
                    if (!data.IsNullOrEmpty())
                    {
                        Logger.Info("Successfully used C+ data from Mare Synchronos.");
                    }
                    else
                    {
                        Logger.Warn("C+ data from Mare Synchronos is also empty. C+ data will not be saved.");
                    }
                }

                if (!data.IsNullOrEmpty())
                {
                    File.WriteAllText(Path.Combine(path, "customizePlus.json"), data);
                }
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string infoJson = JsonSerializer.Serialize(snapshotInfo, options);
            File.WriteAllText(Path.Combine(path, "snapshot.json"), infoJson);

            return true;
        }

        // Helper records for sanitizing the C+ JSON data
        private record BoneTransformSanitizer
        {
            public Vector3? Translation { get; set; }
            public Vector3? Rotation { get; set; }
            public Vector3? Scaling { get; set; }
        }
        private record ProfileSanitizer
        {
            public Dictionary<string, BoneTransformSanitizer> Bones { get; set; } = new();
        }

        public bool LoadSnapshot(ICharacter characterApplyTo, int objIdx, string path)
        {
            Logger.Info($"Applying snapshot to {characterApplyTo.Address}");
            string infoJson = File.ReadAllText(Path.Combine(path, "snapshot.json"));
            if (infoJson == null)
            {
                Logger.Warn("No snapshot json found, aborting");
                return false;
            }
            SnapshotInfo? snapshotInfo = JsonSerializer.Deserialize<SnapshotInfo>(infoJson);
            if (snapshotInfo == null)
            {
                Logger.Warn("Failed to deserialize snapshot json, aborting");
                return false;
            }

            //Apply mods
            Dictionary<string, string> moddedPaths = new();
            foreach (var replacement in snapshotInfo.FileReplacements)
            {
                foreach (var gamePath in replacement.Value)
                {
                    moddedPaths.Add(gamePath, Path.Combine(path, replacement.Key));
                }
            }
            Logger.Debug($"Applied {moddedPaths.Count} replacements");

            Plugin.IpcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.Name.TextValue);
            Plugin.IpcManager.PenumbraSetTempMods(characterApplyTo, objIdx, moddedPaths, snapshotInfo.ManipulationString);
            if (!tempCollections.Contains(characterApplyTo))
            {
                tempCollections.Add(characterApplyTo);
            }

            //Apply Customize+ if it exists and C+ is installed
            if (Plugin.IpcManager.IsCustomizePlusAvailable())
            {
                var cplusPath = Path.Combine(path, "customizePlus.json");
                if (File.Exists(cplusPath))
                {
                    string originalCustPlusData = File.ReadAllText(cplusPath);
                    string sanitizedCustPlusData = SanitizeCustomizePlusJson(originalCustPlusData);
                    if (!string.IsNullOrEmpty(sanitizedCustPlusData))
                    {
                        Plugin.IpcManager.SetCustomizePlusScale(characterApplyTo.Address, sanitizedCustPlusData);
                    }
                }
            }

            //Apply glamourer string
            Plugin.IpcManager.ApplyGlamourerState(snapshotInfo.GlamourerString, characterApplyTo);

            //Redraw
            Plugin.IpcManager.PenumbraRedraw(objIdx);

            return true;
        }

        private string SanitizeCustomizePlusJson(string originalData)
        {
            string jsonToProcess;

            // Step 1: Handle potential Base64 encoding for backward compatibility.
            try
            {
                var bytes = Convert.FromBase64String(originalData);
                jsonToProcess = Encoding.UTF8.GetString(bytes);
                Logger.Debug("Successfully decoded legacy C+ Base64 data.");
            }
            catch (FormatException)
            {
                // If it's not a valid Base64 string, assume it's already raw JSON.
                jsonToProcess = originalData;
                Logger.Debug("C+ data is not Base64, processing as raw JSON.");
            }

            try
            {
                // Step 2: Deserialize using Newtonsoft.Json, which is more forgiving.
                var profile = JsonConvert.DeserializeObject<ProfileSanitizer>(jsonToProcess);

                if (profile?.Bones == null)
                {
                    Logger.Warn($"C+ JSON Sanitizer: Could not deserialize or profile has no bones. JSON: {jsonToProcess}");
                    return string.Empty;
                }

                // Step 3: Rebuild the object with complete data, providing defaults.
                var finalBones = new Dictionary<string, object>();
                foreach (var bone in profile.Bones)
                {
                    var cleanTransforms = new Dictionary<string, Vector3>
                    {
                        ["Translation"] = bone.Value.Translation ?? Vector3.Zero,
                        ["Rotation"] = bone.Value.Rotation ?? Vector3.Zero,
                        ["Scaling"] = bone.Value.Scaling ?? Vector3.One
                    };
                    finalBones[bone.Key] = cleanTransforms;
                }

                var finalProfile = new { Bones = finalBones };

                // Step 4: Serialize back to a clean JSON string using Newtonsoft.
                return JsonConvert.SerializeObject(finalProfile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to sanitize Customize+ JSON. Original Data: {originalData}", ex);
                return string.Empty;
            }
        }


        private int? GetObjIDXFromCharacter(ICharacter character)
        {
            for (var i = 0; i <= Plugin.Objects.Length; i++)
            {
                global::Dalamud.Game.ClientState.Objects.Types.IGameObject current = Plugin.Objects[i];
                if (!(current == null) && current.GameObjectId == character.GameObjectId)
                {
                    return i;
                }
            }
            return null;
        }

        public unsafe List<FileReplacement> GetFileReplacementsForCharacter(ICharacter character)
        {
            List<FileReplacement> replacements = new List<FileReplacement>();
            var charaPointer = character.Address;
            var objectKind = character.ObjectKind;
            var charaName = character.Name.TextValue;
            int? objIdx = GetObjIDXFromCharacter(character);

            Logger.Debug($"Character name {charaName}");
            if (objIdx == null)
            {
                Logger.Error("Unable to find character in object table, aborting search for file replacements");
                return replacements;
            }
            Logger.Debug($"Object IDX {objIdx}");

            var chara = Plugin.DalamudUtil.CreateGameObject(charaPointer)!;
            while (!Plugin.DalamudUtil.IsObjectPresent(chara))
            {
                Logger.Verbose("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }

            Plugin.DalamudUtil.WaitWhileCharacterIsDrawing(objectKind.ToString(), charaPointer, 15000);

            var baseCharacter = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)(void*)charaPointer;
            var human = (Human*)baseCharacter->GameObject.GetDrawObject();

            if (human == null)
            {
                Logger.Error($"Could not get human/draw object for character '{charaName}'. The character is likely not fully rendered. Aborting file replacement scan to prevent a crash.");
                return replacements;
            }

            for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
            {
                var mdl = (Snappy.Interop.RenderModel*)human->CharacterBase.Models[mdlIdx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                AddReplacementsFromRenderModel(mdl, replacements, objIdx.Value, 0);
            }

            AddPlayerSpecificReplacements(replacements, charaPointer, human, objIdx.Value);

            return replacements;
        }

        private unsafe void AddReplacementsFromRenderModel(Snappy.Interop.RenderModel* mdl, List<FileReplacement> replacements, int objIdx, int inheritanceLevel = 0)
        {
            if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
            {
                return;
            }

            string mdlPath;
            try
            {
                mdlPath = new ByteString(mdl->ResourceHandle->FileName()).ToString();
            }
            catch
            {
                Logger.Warn("Could not get model data");
                return;
            }
            Logger.Verbose("Checking File Replacement for Model " + mdlPath);

            FileReplacement mdlFileReplacement = CreateFileReplacement(mdlPath, objIdx);

            AddFileReplacement(replacements, mdlFileReplacement);

            for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
            {
                var mtrl = (Material*)mdl->Materials[mtrlIdx];
                if (mtrl == null) continue;

                AddReplacementsFromMaterial(mtrl, replacements, objIdx, inheritanceLevel + 1);
            }
        }

        private unsafe void AddReplacementsFromMaterial(Material* mtrl, List<FileReplacement> replacements, int objIdx, int inheritanceLevel = 0)
        {
            string fileName;
            try
            {
                fileName = new ByteString(mtrl->ResourceHandle->FileName()).ToString();

            }
            catch
            {
                Logger.Warn("Could not get material data");
                return;
            }

            Logger.Verbose("Checking File Replacement for Material " + fileName);
            var mtrlArray = fileName.Split("|");
            string mtrlPath;
            if (mtrlArray.Count() >= 3)
            {
                mtrlPath = fileName.Split("|")[2];
            }
            else
            {
                Logger.Warn($"Material {fileName} did not split into at least 3 parts");
                return;
            }

            if (replacements.Any(c => c.ResolvedPath.Contains(mtrlPath, StringComparison.Ordinal)))
            {
                return;
            }

            var mtrlFileReplacement = CreateFileReplacement(mtrlPath, objIdx);

            AddFileReplacement(replacements, mtrlFileReplacement);

            var mtrlResourceHandle = (Snappy.Interop.MtrlResource*)mtrl->ResourceHandle;
            for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
            {
                string? texPath = null;
                try
                {
                    texPath = new ByteString(mtrlResourceHandle->TexString(resIdx)).ToString();
                }
                catch
                {
                    Logger.Warn("Could not get Texture data for Material " + fileName);
                }

                if (string.IsNullOrEmpty(texPath)) continue;

                Logger.Verbose("Checking File Replacement for Texture " + texPath);

                AddReplacementsFromTexture(texPath, replacements, objIdx, inheritanceLevel + 1);
            }

            try
            {
                var shpkPath = "shader/sm5/shpk/" + new ByteString(mtrlResourceHandle->ShpkString).ToString();
                Logger.Verbose("Checking File Replacement for Shader " + shpkPath);
                AddReplacementsFromShader(shpkPath, replacements, objIdx, inheritanceLevel + 1);
            }
            catch
            {
                Logger.Verbose("Could not find shpk for Material " + fileName);
            }
        }

        private void AddReplacementsFromTexture(string texPath, List<FileReplacement> replacements, int objIdx, int inheritanceLevel = 0, bool doNotReverseResolve = true)
        {
            if (string.IsNullOrEmpty(texPath) || texPath.Any(c => c < 32 || c > 126))
            {
                Logger.Warn($"Invalid texture path: {texPath}");
                return;
            }

            Logger.Debug($"Adding replacement for texture {texPath}");

            if (replacements.Any(c => c.GamePaths.Contains(texPath, StringComparer.Ordinal)))
            {
                Logger.Debug($"Replacements already contain {texPath}, skipping");
                return;
            }

            var texFileReplacement = CreateFileReplacement(texPath, objIdx, doNotReverseResolve);
            AddFileReplacement(replacements, texFileReplacement);

            if (texPath.Contains("/--", StringComparison.Ordinal)) return;

            var texDx11Replacement = CreateFileReplacement(texPath.Insert(texPath.LastIndexOf('/') + 1, "--"), objIdx, doNotReverseResolve);
            AddFileReplacement(replacements, texDx11Replacement);
        }

        private void AddReplacementsFromShader(string shpkPath, List<FileReplacement> replacements, int objIdx, int inheritanceLevel = 0)
        {
            if (string.IsNullOrEmpty(shpkPath)) return;

            if (replacements.Any(c => c.GamePaths.Contains(shpkPath, StringComparer.Ordinal)))
            {
                return;
            }

            var shpkFileReplacement = CreateFileReplacement(shpkPath, objIdx);
            AddFileReplacement(replacements, shpkFileReplacement);
        }

        private unsafe void AddPlayerSpecificReplacements(List<FileReplacement> replacements, IntPtr charaPointer, Human* human, int objIdx)
        {
            var weaponObject = (Interop.Weapon*)((FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)human)->ChildObject;

            if ((IntPtr)weaponObject != IntPtr.Zero)
            {
                var mainHandWeapon = weaponObject->WeaponRenderModel->RenderModel;

                AddReplacementsFromRenderModel(mainHandWeapon, replacements, objIdx, 0);

                if (weaponObject->NextSibling != (IntPtr)weaponObject)
                {
                    var offHandWeapon = ((Interop.Weapon*)weaponObject->NextSibling)->WeaponRenderModel->RenderModel;

                    AddReplacementsFromRenderModel(offHandWeapon, replacements, objIdx, 1);
                }
            }

            AddReplacementSkeleton(((Interop.HumanExt*)human)->Human.RaceSexId, objIdx, replacements);

            var humanExt = (Interop.HumanExt*)human;
            if (humanExt->Decal != null)
            {
                try
                {
                    AddReplacementsFromTexture(new ByteString(humanExt->Decal->FileName()).ToString(), replacements, objIdx, 0, false);
                }
                catch
                {
                    Logger.Warn("Could not get Decal data (FileName was likely invalid).");
                }
            }

            if (humanExt->LegacyBodyDecal != null)
            {
                try
                {
                    AddReplacementsFromTexture(new ByteString(humanExt->LegacyBodyDecal->FileName()).ToString(), replacements, objIdx, 0, false);
                }
                catch
                {
                    Logger.Warn("Could not get Legacy Body Decal Data (FileName was likely invalid).");
                }
            }
        }

        private void AddReplacementSkeleton(ushort raceSexId, int objIdx, List<FileReplacement> replacements)
        {
            string raceSexIdString = raceSexId.ToString("0000");

            string skeletonPath = $"chara/human/c{raceSexIdString}/skeleton/base/b0001/skl_c{raceSexIdString}b0001.sklb";

            var replacement = CreateFileReplacement(skeletonPath, objIdx, true);
            AddFileReplacement(replacements, replacement);
        }

        private void AddFileReplacement(List<FileReplacement> replacements, FileReplacement newReplacement)
        {
            if (!newReplacement.HasFileReplacement)
            {
                Logger.Debug($"Replacement for {newReplacement.ResolvedPath} does not have a file replacement, skipping");
                foreach (var path in newReplacement.GamePaths)
                {
                    Logger.Debug(path);
                }
                return;
            }

            var existingReplacement = replacements.SingleOrDefault(f => string.Equals(f.ResolvedPath, newReplacement.ResolvedPath, System.StringComparison.OrdinalIgnoreCase));
            if (existingReplacement != null)
            {
                Logger.Debug($"Added replacement for existing path {existingReplacement.ResolvedPath}");
                existingReplacement.GamePaths.AddRange(newReplacement.GamePaths.Where(e => !existingReplacement.GamePaths.Contains(e, System.StringComparer.OrdinalIgnoreCase)));
            }
            else
            {
                Logger.Debug($"Added new replacement {newReplacement.ResolvedPath}");
                replacements.Add(newReplacement);
            }
        }

        private FileReplacement CreateFileReplacement(string path, int objIdx, bool doNotReverseResolve = false)
        {
            var fileReplacement = new FileReplacement(Plugin);

            if (!doNotReverseResolve)
            {
                fileReplacement.ReverseResolvePathObject(path, objIdx);
            }
            else
            {
                fileReplacement.ResolvePathObject(path, objIdx);
            }

            Logger.Debug($"Created file replacement for resolved path {fileReplacement.ResolvedPath}, hash {fileReplacement.Hash}, gamepath {fileReplacement.GamePaths[0]}");
            return fileReplacement;
        }
    }
}