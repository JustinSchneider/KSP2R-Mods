#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Game;
using KSP.Messages;
using WayfarersWings.Models.Session;
using SpaceWarp2.API.Features;

namespace WayfarersWings.Managers
{
    public class MessageListener
    {
        public static MessageListener Instance { get; } = new();

        public static MessageCenter MessageCenter => GameManager.Instance.Game.Messages;

        /// <summary>
        /// Subscribe to messages from the game, without blocking for the needed delay.
        /// </summary>
        public void SubscribeToMessages()
        {
            _ = Subscribe();
        }

        private static async Task Subscribe()
        {
            await Task.Delay(100);

            MessageCenter.PersistentSubscribe<GameLoadFinishedMessage>(OnGameLoadFinishedMessage);

            Core.Instance.EventsRegistry.SetupListeners();
        }

        /// <summary>
        /// Listens for a game message, but only in campaigns that play with the mod. The save's wings are still
        /// loaded into the session regardless (see <see cref="OnGameLoadFinishedMessage"/>), so they save back intact.
        /// </summary>
        public static SubscriptionHandle Subscribe<TMessage>(Action<MessageCenterMessage> callback)
            where TMessage : MessageCenterMessage
        {
            return MessageCenter.PersistentSubscribe<TMessage>(message =>
            {
                if (ActiveFeatures.IsModEnabled(WayfarersWingsRedux.WayfarersWingsReduxPlugin.ModGuid)) callback(message);
            });
        }

        /// <summary>Raised once a save's wings are loaded into the session.</summary>
        public static event Action? SessionLoaded;

        private static void OnGameLoadFinishedMessage(MessageCenterMessage message)
        {
            // Load Wings from WingsConfig
            Core.Instance.WingsPool.LoadRegisteredConfigs();
            // Load GameData
            SaveManager.Instance.LoadGameDataInSession();
            SessionLoaded?.Invoke();
        }

    }
}
