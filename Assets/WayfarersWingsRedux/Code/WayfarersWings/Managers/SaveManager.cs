#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WayfarersWings.Logging;
using SpaceWarp2.API.Backend.SaveGameManager;
using SpaceWarp2.API.SaveGameManager;
using WayfarersWings.Models.Session;

namespace WayfarersWings.Managers
{
    public class SaveManager
    {
        public static SaveManager Instance { get; private set; } = new();
        private readonly ManualLogSource _Logger = Logger.CreateLogSource("WayfarersWings.SaveManager");

        private SaveData? loadedSaveData;

        public void Register()
        {
            ModSaves.RegisterSaveLoadGameData<SaveData>(
                WayfarersWingsRedux.WayfarersWingsReduxPlugin.ModGuid,
                onSave: SaveGameData,
                onLoad: LoadGameData,
                onNew: NewGameData,
                persistenceKind: PersistenceKind.PerSave);
        }

        private void SaveGameData(SaveData dataToSave)
        {
            dataToSave.KerbalProfiles.Clear();
            foreach (var (kerbalId, profile) in WingsSessionManager.Instance.KerbalProfiles)
            {
                dataToSave.KerbalProfiles.Add(profile);
            }
        }

        private SaveData NewGameData(SaveData fresh)
        {
            // A save without wings data: start every kerbal with an empty profile.
            fresh.KerbalProfiles.Clear();
            loadedSaveData = fresh;
            return fresh;
        }

        private void LoadGameData(SaveData dataToLoad)
        {
            loadedSaveData = dataToLoad;
            _Logger.LogInfo("Loaded game data");
        }

        public void LoadGameDataInSession()
        {
            if (loadedSaveData == null)
            {
                _Logger.LogInfo("No save data loaded");
                return;
            }

            var loadedProfiles = new List<KerbalProfile>();
            foreach (var profile in loadedSaveData.KerbalProfiles)
            {
                profile.OnAfterGameLoad();
                loadedProfiles.Add(profile);
            }

            WingsSessionManager.Instance.Initialize(loadedProfiles);
            loadedSaveData = null;
            _Logger.LogInfo("Loaded game data into session");
        }
    }
}
