using System.Linq;
using ThunderKit.Core.Manifests;
using UnityEditor;

/// <summary>
/// Deploy to Zip File stages each mod into Deploy/&lt;id&gt; and zips that folder's contents, so the zip has no
/// top-level folder: unzipping it into mods\ scatters the files instead of creating mods\&lt;id&gt;. Staging one
/// level deeper (Deploy/&lt;id&gt;/&lt;id&gt;) makes the unchanged Zip job produce &lt;id&gt;/swinfo.json and so on.
///
/// Every string in a manifest that starts with "Deploy/" is a staging path (StagingPaths, CopyFolderDatum's
/// destinationPath, AddressablesGroupDatum's targetFolder), so all of them are rewritten. Like
/// TestingAssemblyStaging, this reapplies whenever a manifest is imported, because KSP2 Unity Tools regenerates them.
/// </summary>
[InitializeOnLoad]
public class DeployZipFolder : AssetPostprocessor
{
    private const string DeployRoot = "Deploy/";

    static DeployZipFolder()
    {
        EditorApplication.delayCall += NestAll;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(path => path.EndsWith("Manifest.asset")))
            EditorApplication.delayCall += NestAll;
    }

    [MenuItem("Tools/Put Deploy Zip Contents in a Mod Folder")]
    private static void NestAll()
    {
        bool changed = false;
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(Manifest)))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null) continue;
                var serialized = new SerializedObject(asset);
                var property = serialized.GetIterator();
                bool assetChanged = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.String) continue;
                    var nested = Nest(property.stringValue);
                    if (nested == property.stringValue) continue;
                    property.stringValue = nested;
                    assetChanged = true;
                }
                if (!assetChanged) continue;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                changed = true;
            }
        }
        if (changed) AssetDatabase.SaveAssets();
    }

    /// <summary>Deploy/&lt;id&gt;[/rest] becomes Deploy/&lt;id&gt;/&lt;id&gt;[/rest]; anything else, or an already nested path, is unchanged.</summary>
    private static string Nest(string staging)
    {
        if (string.IsNullOrEmpty(staging) || !staging.Replace('\\', '/').StartsWith(DeployRoot)) return staging;
        var parts = staging.Replace('\\', '/').TrimEnd('/').Split('/');
        if (parts.Length < 2 || parts[1].Length == 0) return staging;
        if (parts.Length > 2 && parts[2] == parts[1]) return staging;
        return string.Join("/", new[] { parts[0], parts[1], parts[1] }.Concat(parts.Skip(2)));
    }
}
