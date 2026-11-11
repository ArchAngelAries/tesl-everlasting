using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using SparkleServices;

namespace OfflineEngine
{
    // Original menu and confirmation UI, with a local retry callback and honest
    // offline identity presentation. Does not create authentication/device data.
    public static class OfflineNativeOptions
    {
        private static bool installed;
        private static Action restart;

        public static void Install(Harmony harmony, Action requestRestart)
        {
            if (!OfflineBootstrapPlugin.Active) throw new InvalidOperationException("Native offline options require guarded local startup.");
            if (harmony == null) throw new ArgumentNullException("harmony");
            if (requestRestart == null) throw new ArgumentNullException("requestRestart");
            restart = requestRestart;
            if (installed) return;
            MethodInfo awake = AccessTools.Method(typeof(OptionsWin), "Awake", Type.EmptyTypes);
            MethodInfo option = AccessTools.Method(typeof(OptionsController), "HandleOnOption", new Type[] { typeof(OptionsData.Option) });
            if (awake == null || option == null) throw new MissingMethodException("Original options entry points are absent.");
            harmony.Patch(awake, null, null, new HarmonyMethod(typeof(OfflineNativeOptions), "PreserveNativeAwakeWithLocalIdentity"), null, null);
            harmony.Patch(option, new HarmonyMethod(typeof(OfflineNativeOptions), "HandleLocalRetry"), null, null, null, null);
            installed = true;
        }

        private static IEnumerable<CodeInstruction> PreserveNativeAwakeWithLocalIdentity(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo buid = AccessTools.Field(typeof(AuthData), "buid");
            MethodInfo device = AccessTools.Method(typeof(BNETAuthUtilities), "GetDeviceId", Type.EmptyTypes);
            MethodInfo localAccount = AccessTools.Method(typeof(OfflineNativeOptions), "DisplayAccountId");
            MethodInfo localDevice = AccessTools.Method(typeof(OfflineNativeOptions), "DisplayDeviceId");
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int accounts = 0, devices = 0;
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.opcode == OpCodes.Ldfld && object.Equals(instruction.operand, buid))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = localAccount;
                    accounts++;
                }
                else if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && object.Equals(instruction.operand, device))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = localDevice;
                    devices++;
                }
            }
            if (accounts != 1 || devices != 1)
                throw new InvalidOperationException("Original OptionsWin.Awake identity call sites changed: account=" + accounts + ", device=" + devices);
            return code;
        }

        private static string DisplayAccountId(AuthData value)
        {
            return OfflineBootstrapPlugin.Active ? "offline" : value.buid;
        }

        private static string DisplayDeviceId()
        {
            return OfflineBootstrapPlugin.Active ? "local" : BNETAuthUtilities.GetDeviceId();
        }

        private static bool HandleLocalRetry(OptionsController __instance, OptionsData.Option __0)
        {
            if (!OfflinePracticeSession.Enabled || __0 != OptionsData.Option.Retry) return true;
            if (restart == null) throw new InvalidOperationException("The offline retry callback is absent.");
            FieldInfo winField = AccessTools.Field(typeof(OptionsController), "_optionsWin");
            if (winField == null || winField.FieldType != typeof(OptionsWin))
                throw new MissingFieldException(typeof(OptionsController).FullName, "_optionsWin");
            OptionsWin window = winField.GetValue(__instance) as OptionsWin;
            if (window == null) throw new InvalidOperationException("The original options window is absent.");
            OptionsData.Option[] previousOptions = window.GetOptions();
            __instance.ToggleWindow(null);
            Singleton<WinManager>.Instance.Popup.Show(
                LocalizationManager.Instance.GetText("retryMatch.confirm.title"),
                LocalizationManager.Instance.GetText("retryMatch.confirm.message"), null, null,
                LocalizationManager.Instance.GetText(!PlatformUtils.IsGamepadAllowed() ? LocalizationKeys.Common_Dialog_Ok : LocalizationKeys.Common_Dialog_Ok_Gamepad),
                LocalizationManager.Instance.GetText(!PlatformUtils.IsGamepadAllowed() ? LocalizationKeys.Common_Dialog_Cancel : LocalizationKeys.Common_Dialog_Cancel_Gamepad),
                true, true,
                delegate
                {
                    // Native retry info describes a remote Matching request.
                    // The parent's callback reuses validated local selected decks.
                    Singleton<SettingsManager>.Instance.LastRetryMatchRequestData.IsRetryRequested = false;
                    restart();
                },
                delegate { if (previousOptions != null) __instance.ShowWindow(previousOptions); }, null);
            return false;
        }
    }
}
