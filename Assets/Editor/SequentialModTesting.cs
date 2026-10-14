using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ksp2UnityTools.Editor;
using Ksp2UnityTools.Editor.Modding;
using ThunderKit.Core.Pipelines;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A replacement for KSP2 Unity Tools' "Test Selected Mods In Editor" that works with more than one mod ticked.
///
/// ThunderKit ends every pipeline with CompilationPipeline.RequestScriptCompilation(). The stock button runs the
/// mods' pipelines back to back, so the first mod's recompile and domain reload land in the middle of the second
/// mod's build. That kills the async test task: the build never finishes and Play mode never starts.
///
/// This builds one mod at a time and keeps the queue in SessionState, which survives domain reloads. After each
/// build it waits for any recompile; if a reload happens, it carries on from the queue once the domain is back.
/// </summary>
[InitializeOnLoad]
public static class SequentialModTesting
{
    private const string QueueKey = "SequentialModTesting.Queue";
    // Set for the whole run, so a reload after the last build still resumes to enter Play mode.
    private const string ActiveKey = "SequentialModTesting.Active";
    private const string TestingFolder = "Assets/Mods/__Testing";
    private const string BootScene = "Assets/boot-ksp.unity";
    // Long enough for a requested compile to start, which happens on a later editor update.
    private const int SettleMilliseconds = 1500;

    static SequentialModTesting()
    {
        // A domain reload kills a running pipeline before ThunderKit can close its progress bar, which then sits
        // on screen looking like a hang. Nothing from before the reload is still running, so it's safe to clear.
        EditorUtility.ClearProgressBar();

        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.delayCall += () => _ = Continue();
    }

    [MenuItem("Tools/Test Ticked Mods In Editor (One At A Time)")]
    private static void Start()
    {
        var mods = TickedMods();
        if (mods.Count == 0)
        {
            EditorUtility.DisplayDialog("Test Ticked Mods",
                "Tick mods in Modding → KSP2 Unity Tools, or select their swinfo assets in the Project window.", "OK");
            return;
        }

        // Same clean start as the stock button.
        if (Directory.Exists(TestingFolder)) Directory.Delete(TestingFolder, true);
        if (File.Exists(TestingFolder + ".meta")) File.Delete(TestingFolder + ".meta");

        SessionState.SetString(QueueKey, string.Join("|", mods.Select(m => m.Folder)));
        SessionState.SetBool(ActiveKey, true);
        Debug.Log($"[SequentialModTesting] Testing {string.Join(", ", mods.Select(m => m.name))}");

        // Pick up script edits now: the pipelines refresh the asset database themselves, and compiling a pending
        // edit then reloads the domain mid-build. If this refresh reloads instead, the queue resumes afterwards.
        AssetDatabase.Refresh();
        _ = Continue();
    }

    private static async Task Continue()
    {
        var queue = SessionState.GetString(QueueKey, "").Split('|').Where(f => f.Length > 0).ToList();
        while (queue.Count > 0)
        {
            await WaitForIdle();

            // Take the mod off the queue before building, so a reload mid-build can't loop forever on it.
            string folder = queue[0];
            queue.RemoveAt(0);
            SessionState.SetString(QueueKey, string.Join("|", queue));

            var pipeline = AssetDatabase.LoadAssetAtPath<Pipeline>(folder + "/Pipelines/Build for Editor.asset");
            if (pipeline == null)
            {
                Debug.LogError($"[SequentialModTesting] No Build for Editor pipeline in {folder}; skipping it");
                continue;
            }
            Debug.Log($"[SequentialModTesting] Building {folder}");
            try
            {
                await pipeline.Execute();
            }
            catch (System.Exception e)
            {
                // Don't leave a run behind that the next domain reload would pick up.
                EndRun();
                Debug.LogError($"[SequentialModTesting] Build failed for {folder}; stopping. {e}");
                return;
            }

            // If the pipeline's recompile reloads the domain during this wait, the static constructor resumes.
            await Task.Delay(SettleMilliseconds);
        }

        await WaitForIdle();
        EndRun();
        Debug.Log("[SequentialModTesting] All builds done; entering Play mode");
        EditorSceneManager.OpenScene(BootScene);
        EditorApplication.EnterPlaymode();
    }

    private static void EndRun()
    {
        SessionState.EraseString(QueueKey);
        SessionState.EraseBool(ActiveKey);
    }

    private static async Task WaitForIdle()
    {
        while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            await Task.Delay(250);
    }

    /// <summary>The mods ticked in an open KSP2 Unity Tools window, or else the Mod assets selected in the Project window.</summary>
    private static List<Mod> TickedMods()
    {
        var field = typeof(KSP2UnityToolsWindow).GetField("_selectedMods", BindingFlags.Instance | BindingFlags.NonPublic);
        var window = Resources.FindObjectsOfTypeAll<KSP2UnityToolsWindow>().FirstOrDefault();
        if (field != null && window != null && field.GetValue(window) is List<Mod> ticked && ticked.Count > 0)
            return ticked.Where(m => m != null).ToList();
        return Selection.GetFiltered<Mod>(SelectionMode.Assets).ToList();
    }
}
