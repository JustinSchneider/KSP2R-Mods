using System.Linq;
using ThunderKit.Core.Manifests;
using ThunderKit.Core.Manifests.Datums;
using UnityEditor;

/// <summary>
/// Test in Editor's pipeline stages each mod's DLL into Assets/Mods/__Testing/&lt;id&gt;. Inside Assets, Unity imports
/// that copy as a plugin next to the asmdef that built it: it logs "has the same filename as Assembly Definition
/// File", and the import starts a recompile that StageAssemblies then waits on, so the build hangs.
///
/// The Editor never needs that copy: SpaceWarp2 uses the already-loaded assembly with the mod's name when running
/// in the Editor. So stage it into a "bin~" subfolder, which Unity ignores. KSP2 Unity Tools regenerates the
/// pipelines (Refresh Thunderkit Pipelines, Create New Mod), so this reapplies whenever a manifest is imported.
/// </summary>
[InitializeOnLoad]
public class TestingAssemblyStaging : AssetPostprocessor
{
    private const string TestingRoot = "Assets/Mods/__Testing/";
    private const string HiddenFolder = "/bin~";

    static TestingAssemblyStaging()
    {
        EditorApplication.delayCall += RedirectAll;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(path => path.EndsWith("Manifest.asset")))
            EditorApplication.delayCall += RedirectAll;
    }

    [MenuItem("Tools/Redirect Test-in-Editor Assembly Staging")]
    private static void RedirectAll()
    {
        bool changed = false;
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(Manifest)))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var datum in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AssemblyDefinitions>())
            {
                if (datum.StagingPaths == null) continue;
                for (int i = 0; i < datum.StagingPaths.Length; i++)
                {
                    var staging = datum.StagingPaths[i];
                    if (staging == null || !staging.StartsWith(TestingRoot) || staging.Contains("~")) continue;
                    datum.StagingPaths[i] = staging.TrimEnd('/') + HiddenFolder;
                    EditorUtility.SetDirty(datum);
                    changed = true;
                }
            }
        }
        if (changed) AssetDatabase.SaveAssets();
    }
}
