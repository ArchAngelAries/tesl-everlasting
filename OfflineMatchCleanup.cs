using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using SparkleServices;

namespace OfflineEngine
{
    public static class OfflineMatchCleanup
    {
        // Call after EndConnectionToServer and immediately before a new launch.
        // Capture/show the previous native result before clearing completion.
        public static void ResetForNextMatch(GameNetworkManager network)
        {
            RequireOffline();
            if (network == null) throw new ArgumentNullException("network");
            if (!network.IsOutsideOfMatch()
                || RequiredField(typeof(GameNetworkManager), "_tcpClient", typeof(TcpNetworkClient)).GetValue(network) != null
                || RequiredField(typeof(GameNetworkManager), "_tcpServer", typeof(TcpNetworkServer)).GetValue(network) != null
                || RequiredField(typeof(GameNetworkManager), "_embeddedMatchServer", typeof(Simulation)).GetValue(network) != null)
                throw new InvalidOperationException("Close the original local transport before resetting replay state.");
            string[] boolNames = { "_readyToShowPlaymat", "_everConnected", "_reconnectTested", "_isSpectating", "_isDualSpectating", "ForceClientDisconnect" };
            FieldInfo[] boolFields = new FieldInfo[boolNames.Length];
            for (int i = 0; i < boolNames.Length; i++)
                boolFields[i] = RequiredField(typeof(GameNetworkManager), boolNames[i], typeof(bool));
            FieldInfo matchId = RequiredField(typeof(GameNetworkManager), "_matchId", typeof(string));
            FieldInfo matchType = RequiredField(typeof(GameNetworkManager), "_matchType", typeof(Matching.IMatchType));
            FieldInfo sceneClient = RequiredField(typeof(GameNetworkManager), "_matchNetworkClient", typeof(MatchNetworkClient));
            FieldInfo version = RequiredField(typeof(GameNetworkManager), "CurrentGameserverVersion", typeof(string));
            Stopwatch awaitResponse = RequiredField(typeof(GameNetworkManager), "AwaitResponseStopwatch", typeof(Stopwatch)).GetValue(network) as Stopwatch;
            Stopwatch connection = RequiredField(typeof(GameNetworkManager), "ConnectionCompleteStopwatch", typeof(Stopwatch)).GetValue(network) as Stopwatch;
            SettingsManager settings = Singleton<SettingsManager>.Instance;
            if (awaitResponse == null || connection == null || settings == null)
                throw new InvalidOperationException("Native replay stopwatches/settings are absent.");
            foreach (FieldInfo field in boolFields) field.SetValue(network, false);
            matchId.SetValue(network, null);
            matchType.SetValue(network, Matching.IMatchType.Solo);
            sceneClient.SetValue(network, null);
            version.SetValue(network, string.Empty);
            awaitResponse.Stop(); awaitResponse.Reset();
            connection.Stop(); connection.Reset();
            settings.GameCompletionType = SettingsData.EndGameType.None;
            UnityEngine.Debug.Log("OFFLINE REPLAY: stale native connection/playmat/result state cleared.");
        }

        public static void DetachOutgoingBoard(BoardConductor conductor)
        {
            RequireOffline();
            if (conductor == null) return;
            MatchUI active = conductor.BoardState == null ? null : conductor.BoardState.MatchUI;
            MatchUI[] matches = { active, conductor.MatchUI_PC, conductor.MatchUI_Mobile };
            int detached = 0;
            for (int i = 0; i < matches.Length; i++)
            {
                MatchUI match = matches[i];
                if (match == null) continue;
                bool seen = false;
                for (int j = 0; j < i; j++) if (object.ReferenceEquals(matches[j], match)) seen = true;
                if (seen) continue;
                UiInputController input = RequiredField(typeof(MatchUI), "_uiInputController", typeof(UiInputController)).GetValue(match) as UiInputController;
                if (input == null) continue; // Inactive layout was never initialized.
                MethodInfo options = RequiredMethod(typeof(MatchUI), "HandleOnOptions");
                FriendsWin friends = RequiredField(typeof(MatchUI), "_friendsWin", typeof(FriendsWin)).GetValue(match) as FriendsWin;
                detached += RemoveExact(input, "OnOptions", match, options);
                detached += RemoveExact(input, "OnGamepadOptions", match, options);
                if (friends != null)
                    detached += RemoveExact(input, "OnEnter", friends, RequiredMethod(typeof(FriendsWin), "ToggleChatWin"));
            }
            UnityEngine.Debug.Log("OFFLINE REPLAY: detached " + detached + " outgoing native board input subscriptions.");
        }

        private static int RemoveExact(UiInputController input, string fieldName, object target, MethodInfo method)
        {
            FieldInfo field = RequiredField(typeof(UiInputController), fieldName, typeof(Action));
            Action callbacks = field.GetValue(input) as Action;
            if (callbacks == null) return 0;
            int removed = 0;
            foreach (Delegate callback in callbacks.GetInvocationList())
                if (object.ReferenceEquals(callback.Target, target) && callback.Method == method)
                {
                    callbacks = (Action)Delegate.Remove(callbacks, callback);
                    removed++;
                }
            field.SetValue(input, callbacks);
            return removed;
        }

        private static FieldInfo RequiredField(Type type, string name, Type fieldType)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequiredMethod(Type type, string name)
        {
            MethodInfo method = AccessTools.Method(type, name, Type.EmptyTypes);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static void RequireOffline()
        {
            if (!OfflineBootstrapPlugin.Active) throw new InvalidOperationException("Local replay cleanup requires guarded offline startup.");
        }
    }
}
