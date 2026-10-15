using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.Game;
using KSP.Sim.Definitions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgencyHeritage
{
    /// <summary>
    /// A blueprint as shown in an era's gallery, with its spec sheet. Saved with an era's archive, so keep it plain
    /// fields; anything not known is left at zero or null and its line is left off the sheet.
    /// </summary>
    public class BlueprintCard
    {
        public string Name;
        public string VehicleName;
        public int Parts;
        public DateTime SaveTime;
        public string Description;
        /// <summary>Tonnes, as the VAB reported it.</summary>
        public double Mass;
        public int Stages;
        /// <summary>Metres: the widest horizontal extent, and the height.</summary>
        public float Width, Height;
        public int CrewSeats;
        /// <summary>Engines by part name (internal, e.g. "engine_2v_methalox_mainsail"), most first.</summary>
        public List<PartTally> Engines = new List<PartTally>();
        /// <summary>Propellant by resource, in tonnes; methane and oxidizer are combined as Methalox.</summary>
        public List<PartTally> Propellant = new List<PartTally>();
        public double Battery;
        public int SolarPanels, Generators, Alternators, Parachutes, Experiments, Antennas, RcsThrusters;
        /// <summary>From the VAB at the moment it was saved; 0 if it was saved before the mod could see it.</summary>
        public double DeltaVVac, DeltaVAsl, BurnTime;
        public float LaunchTwr;
        /// <summary>Full path of the thumbnail PNG; null if there is none.</summary>
        [JsonIgnore] public string ThumbnailPath;
        /// <summary>The workspace file this card was read from; null for an ended era's archived card.</summary>
        [JsonIgnore] public string WorkspacePath;
    }

    /// <summary>A count or amount of one kind of thing on a craft.</summary>
    public class PartTally
    {
        public string Name;
        public double Amount;
    }

    /// <summary>
    /// Finds the campaign's blueprints and keeps an archive of each ended era's thumbnails, so a blueprint that was
    /// later re-saved in a new livery still shows its old look under the old era.
    /// Layout: &lt;Campaign&gt;/AgencyHeritage/eras/&lt;eraId&gt;/{cards.json, &lt;Name&gt;.png}
    /// </summary>
    public static class BlueprintArchive
    {
        private const string CardsFile = "cards.json";

        /// <summary>Spec sheets already read, by workspace file and save time: the workspace files are large.</summary>
        private static readonly Dictionary<(string, long), BlueprintCard> SpecCache = new Dictionary<(string, long), BlueprintCard>();

        /// <summary>Every saved (non-autosave) blueprint in the campaign's Workspaces folder.</summary>
        public static List<BlueprintCard> ScanWorkspaces(string workspaceFolder, HeritageSaveData data)
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
                    string json = Path.ChangeExtension(metaPath, ".json");
                    if (!SpecCache.TryGetValue((json, meta.SaveTime), out var card))
                    {
                        card = new BlueprintCard
                        {
                            Name = string.IsNullOrEmpty(meta.Name) ? Path.GetFileNameWithoutExtension(metaPath) : meta.Name,
                            VehicleName = meta.VehicleName,
                            Parts = meta.Parts,
                            // SaveTime is DateTime ticks in local time.
                            SaveTime = new DateTime(meta.SaveTime, DateTimeKind.Local),
                            Description = string.IsNullOrWhiteSpace(meta.Description) ? null : meta.Description.Trim(),
                            Mass = meta.Mass,
                            Stages = meta.Stages,
                        };
                        ReadSpecs(card, json);
                        SpecCache[(json, meta.SaveTime)] = card;
                    }
                    card.ThumbnailPath = File.Exists(png) ? png : null;
                    card.WorkspacePath = File.Exists(json) ? json : null;
                    // Performance figures the mod caught from the VAB when this version of the blueprint was saved.
                    var stats = data?.Blueprints.Find(b => b.Workspace == card.Name && b.SaveTime == meta.SaveTime);
                    if (stats != null)
                    {
                        card.DeltaVVac = stats.DeltaVVac;
                        card.DeltaVAsl = stats.DeltaVAsl;
                        card.LaunchTwr = stats.LaunchTwr;
                        card.BurnTime = stats.BurnTime;
                    }
                    cards.Add(card);
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

            var cards = ScanWorkspaces(workspaceFolder, data);
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

        /// <summary>
        /// Fills in a card's spec sheet from the workspace file: its size, crew seats, engines, propellant, power and
        /// other systems. A file that can't be read just leaves those lines off.
        /// </summary>
        private static void ReadSpecs(BlueprintCard card, string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(jsonPath));
                var main = root["Assemblies"]?.FirstOrDefault(a => a?["isMainAssembly"]?.Value<bool>() == true)
                    ?? root["Assemblies"]?.FirstOrDefault();
                if (main == null) return;
                var size = main["boundsSize"];
                if (size != null)
                {
                    card.Width = Math.Max(size["x"]?.Value<float>() ?? 0, size["z"]?.Value<float>() ?? 0);
                    card.Height = size["y"]?.Value<float>() ?? 0;
                }

                var seats = PartCrewCapacity;
                var engines = new Dictionary<string, int>();
                var propellant = new Dictionary<string, double>();
                foreach (var part in main["Assembly"]?["parts"] ?? new JArray())
                {
                    string name = part["partName"]?.Value<string>();
                    var modules = new HashSet<string>((part["PartModulesState"] ?? new JArray())
                        .Select(m => m?["Name"]?.Value<string>()).Where(m => m != null));

                    if (name != null && seats != null && modules.Contains("PartComponentModule_CrewedInterior"))
                    {
                        int? capacity = seats(name);
                        if (capacity.HasValue) card.CrewSeats += capacity.Value;
                        else SWLog.Warning($"Couldn't look up part {name} for its crew seats");
                    }
                    bool engine = modules.Contains("PartComponentModule_Engine");
                    if (engine && name != null) engines[name] = engines.TryGetValue(name, out int n) ? n + 1 : 1;
                    if (modules.Contains("PartComponentModule_SolarPanel")) card.SolarPanels++;
                    // A generator on an engine is its alternator; anywhere else it's an RTG or the like.
                    if (modules.Contains("PartComponentModule_Generator")) { if (engine) card.Alternators++; else card.Generators++; }
                    if (modules.Contains("PartComponentModule_Parachute")) card.Parachutes++;
                    if (modules.Contains("PartComponentModule_ScienceExperiment")) card.Experiments++;
                    if (modules.Contains("PartComponentModule_DataTransmitter")) card.Antennas++;
                    if (modules.Contains("PartComponentModule_RCS")) card.RcsThrusters++;

                    if (part["partState"]?["resources"] is JObject resources)
                        foreach (var resource in resources.Properties())
                        {
                            string id = resource.Value["name"]?.Value<string>() ?? resource.Name;
                            double units = resource.Value["capacityUnits"]?.Value<double>() ?? 0;
                            if (id == "ElectricCharge") { card.Battery += units; continue; }
                            // Stock propellants weigh a tonne per unit; mixing methane and oxidizer reads as Methalox.
                            string key = id == "Methane" || id == "Oxidizer" ? "Methalox" : id;
                            propellant[key] = (propellant.TryGetValue(key, out double t) ? t : 0) + units * MassPerUnit(id);
                        }
                }
                card.Engines = engines.OrderByDescending(e => e.Value).ThenBy(e => e.Key)
                    .Select(e => new PartTally { Name = e.Key, Amount = e.Value }).ToList();
                card.Propellant = propellant.Where(p => p.Value > 0.005).OrderByDescending(p => p.Value)
                    .Select(p => new PartTally { Name = p.Key, Amount = p.Value }).ToList();
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't read the spec sheet of {jsonPath}: {e.Message}");
            }
        }

        private static GameInstance Game => GameManager.Instance?.Game;

        /// <summary>A part's crew seats by part name, or null if the game doesn't know the part. Set by the plugin.</summary>
        internal static Func<string, int?> PartCrewCapacity;

        /// <summary>The main assembly of a workspace file, exactly as the game wrote it, or null.</summary>
        public static JToken MainAssembly(string workspacePath)
        {
            if (workspacePath == null || !File.Exists(workspacePath)) return null;
            var root = JObject.Parse(File.ReadAllText(workspacePath));
            var main = root["Assemblies"]?.FirstOrDefault(a => a?["isMainAssembly"]?.Value<bool>() == true)
                ?? root["Assemblies"]?.FirstOrDefault();
            return main?["Assembly"];
        }

        /// <summary>The resource's tonnes per unit, from the game when it knows; stock fuels are all 1.</summary>
        private static double MassPerUnit(string resource)
        {
            try
            {
                var database = Game?.ResourceDefinitionDatabase;
                if (database != null)
                {
                    var id = database.GetResourceIDFromName(resource);
                    var definition = database.GetDefinitionData(id);
                    if (definition != null) return definition.resourceProperties.massPerUnit;
                }
            }
            catch (Exception) { /* fall back to the stock value */ }
            return 1;
        }

        /// <summary>The fields of a workspace .meta file we use (the game's OABSavedWorkspaceMetaInfo).</summary>
        private class WorkspaceMeta
        {
            public string Name;
            public string VehicleName;
            public string Description;
            public double Mass;
            public long SaveTime;
            public int Parts;
            public int Stages;
            public bool IsAutoSave;
            public bool IsBackupWorkspace;
        }
    }
}
