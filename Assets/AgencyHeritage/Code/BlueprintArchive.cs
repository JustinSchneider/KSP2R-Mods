using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AgencyHeritage
{
    /// <summary>A blueprint as shown in an era's gallery.</summary>
    public class BlueprintCard
    {
        public string Name;
        public string VehicleName;
        public int Parts;
        public DateTime SaveTime;
        /// <summary>Full path of the thumbnail PNG; null if there is none.</summary>
        [JsonIgnore] public string ThumbnailPath;
    }

    /// <summary>
    /// Finds the campaign's blueprints and keeps an archive of each ended era's thumbnails, so a blueprint that was
    /// later re-saved in a new livery still shows its old look under the old era.
    /// Layout: &lt;Campaign&gt;/AgencyHeritage/eras/&lt;eraId&gt;/{cards.json, &lt;Name&gt;.png}
    /// </summary>
    public static class BlueprintArchive
    {
        private const string CardsFile = "cards.json";

        /// <summary>Every saved (non-autosave) blueprint in the campaign's Workspaces folder.</summary>
        public static List<BlueprintCard> ScanWorkspaces(string workspaceFolder)
        {
            var cards = new List<BlueprintCard>();
            if (string.IsNullOrEmpty(workspaceFolder) || !Directory.Exists(workspaceFolder)) return cards;

            foreach (var metaPath in Directory.GetFiles(workspaceFolder, "*.meta"))
            {
                try
                {
                    var meta = JsonConvert.DeserializeObject<WorkspaceMeta>(File.ReadAllText(metaPath));
                    if (meta == null || meta.IsAutoSave || meta.IsBackupWorkspace) continue;
                    string png = Path.ChangeExtension(metaPath, ".png");
                    cards.Add(new BlueprintCard
                    {
                        Name = string.IsNullOrEmpty(meta.Name) ? Path.GetFileNameWithoutExtension(metaPath) : meta.Name,
                        VehicleName = meta.VehicleName,
                        Parts = meta.Parts,
                        // SaveTime is DateTime ticks in local time.
                        SaveTime = new DateTime(meta.SaveTime, DateTimeKind.Local),
                        ThumbnailPath = File.Exists(png) ? png : null,
                    });
                }
                catch (Exception e)
                {
                    SWLog.Warning($"Skipping unreadable workspace metadata {metaPath}: {e.Message}");
                }
            }
            cards.Sort((a, b) => a.SaveTime.CompareTo(b.SaveTime));
            return cards;
        }

        /// <summary>The blueprints that belong to an era: archived ones if it has ended, otherwise live workspaces.</summary>
        public static List<BlueprintCard> CardsForEra(HeritageSaveData data, BrandEra era, string campaignFolder, string workspaceFolder)
        {
            if (era.ThumbnailsArchived)
                return LoadArchive(EraFolder(campaignFolder, era));

            var cards = ScanWorkspaces(workspaceFolder);
            cards.RemoveAll(c => data.EraAtRealTime(c.SaveTime) != era);
            return cards;
        }

        /// <summary>Copies the ending era's blueprint thumbnails into its archive folder.</summary>
        public static void ArchiveEra(HeritageSaveData data, BrandEra era, string campaignFolder, string workspaceFolder)
        {
            var cards = CardsForEra(data, era, campaignFolder, workspaceFolder);
            string folder = EraFolder(campaignFolder, era);
            Directory.CreateDirectory(folder);

            foreach (var card in cards)
            {
                if (card.ThumbnailPath == null) continue;
                File.Copy(card.ThumbnailPath, Path.Combine(folder, SafeFileName(card.Name) + ".png"), true);
            }
            File.WriteAllText(Path.Combine(folder, CardsFile), JsonConvert.SerializeObject(cards, Formatting.Indented));
            era.ThumbnailsArchived = true;
        }

        private static List<BlueprintCard> LoadArchive(string folder)
        {
            string path = Path.Combine(folder, CardsFile);
            if (!File.Exists(path)) return new List<BlueprintCard>();

            var cards = JsonConvert.DeserializeObject<List<BlueprintCard>>(File.ReadAllText(path)) ?? new List<BlueprintCard>();
            foreach (var card in cards)
            {
                string png = Path.Combine(folder, SafeFileName(card.Name) + ".png");
                card.ThumbnailPath = File.Exists(png) ? png : null;
            }
            return cards;
        }

        private static string EraFolder(string campaignFolder, BrandEra era) =>
            Path.Combine(campaignFolder, "AgencyHeritage", "eras", era.Id);

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>The fields of a workspace .meta file we use (the game's OABSavedWorkspaceMetaInfo).</summary>
        private class WorkspaceMeta
        {
            public string Name;
            public string VehicleName;
            public long SaveTime;
            public int Parts;
            public bool IsAutoSave;
            public bool IsBackupWorkspace;
        }
    }
}
