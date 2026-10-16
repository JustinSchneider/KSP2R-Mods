#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Game;

namespace WayfarersWings.Managers
{
    public class GameStateManager
    {
        private static readonly int[] InvalidStates =
        {
            (int)GameState.Flag,
            (int)GameState.MainMenu,
            (int)GameState.Loading,
            (int)GameState.WarmUpLoading,
            (int)GameState.Invalid
        };

        public static bool IsInvalidState()
        {
            var gameState = GameManager.Instance.Game.GlobalGameState.GetGameState()?.GameState;
            return gameState == null || InvalidStates.Contains((int)gameState);
        }

        public static bool CanShowFlightReport()
        {
            var gameState = GameManager.Instance.Game.GlobalGameState.GetGameState().GameState;
            // ReSharper disable once Unity.NoNullPropagation
            return Utility.GameInternals.CanShowFlightReport(GameManager.Instance.Game.FlightReport, gameState);
        }
    }
}
