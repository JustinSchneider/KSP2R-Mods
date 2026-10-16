using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Puts Wayfarer's Wings Redux's ribbon art and wing definitions in the mod's addressables group, under the
/// addresses and labels the original mod used. The wing JSON names its image layers by those addresses
/// ("Assets/Wings/Layers/x.png"), so keeping them lets the JSON stay identical to upstream.
/// Run it again after adding or renaming a ribbon layer or a definition file.
/// </summary>
public static class WingsAddressablesSync
{
    private const string ModRoot = "Assets/WayfarersWingsRedux/";
    private const string GroupName = "addressables_WayfarersWingsRedux_all";
    private const string DataLabel = "wayfarer_wings_data";
    private const string PlanetsLabel = "wayfarer_wings_planets";
    private const string ImagesLabel = "wayfarer_wings_images";

    [MenuItem("Tools/Wayfarer's Wings/Sync Addressables")]
    public static void Sync()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(GroupName) : null;
        if (group == null)
        {
            Debug.LogError($"[WingsAddressablesSync] Addressables group {GroupName} not found");
            return;
        }

        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { ModRoot + "Wings" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string label = extension == ".png" ? ImagesLabel
                : extension != ".json" ? null
                : path.Contains("/Planets/") ? PlanetsLabel
                : DataLabel;
            if (label == null) continue;

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            // "Assets/WayfarersWingsRedux/Wings/Layers/x.png" -> "Assets/Wings/Layers/x.png"
            entry.address = "Assets/" + path.Substring(ModRoot.Length);
            foreach (var old in entry.labels.ToList()) entry.SetLabel(old, false, false, false);
            entry.SetLabel(label, true, true, false);
            count++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WingsAddressablesSync] {count} entries in {GroupName}");
    }
}
