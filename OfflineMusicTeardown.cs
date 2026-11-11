using System;
using System.Reflection;
using DarkTonic.MasterAudio;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OfflineEngine
{
    // Invoke inside the completed original fade callback, BEFORE Release of
    // preparation references and BEFORE SceneManager.LoadScene. No scene hooks,
    // singleton replacement or native generation changes are introduced here.
    public static class OfflineMusicTeardown
    {
        public static void BeforeSceneUnload()
        {
            if (!OfflineBootstrapPlugin.Active || !OfflineBoardAdapter.IsBoardMode)
                throw new InvalidOperationException("Music scene teardown requires guarded offline board mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || (scene.name != "MainMenu" && scene.name != "CDGameBoard"))
                throw new InvalidOperationException("Music teardown requires the outgoing original menu or board scene to remain loaded.");
            AudioAssetManager audio = Singleton<AudioAssetManager>.Instance;
            MusicAssetManager manager = audio == null ? null : audio.MusicManager;
            MasterAudio master = MasterAudio.SafeInstance;
            if (manager == null || master == null || !manager.gameObject.scene.Equals(scene)
                || !master.gameObject.scene.Equals(scene))
                throw new InvalidOperationException("Outgoing native audio hierarchy is absent or belongs to a different scene.");
            FieldInfo receiptField = RequiredField(typeof(AudioPlaybackManager), "_lastMusicReceipt", typeof(MusicAssetManager.Receipt));
            FieldInfo musicField = RequiredField(typeof(AudioPlaybackManager), "_lastBgMusic", typeof(AudioBgMusicData));
            FieldInfo playField = RequiredField(typeof(AudioPlaybackManager), "_playlistPlayRoutine", typeof(Coroutine));
            FieldInfo endField = RequiredField(typeof(AudioPlaybackManager), "_playlistEndRoutine", typeof(Coroutine));
            FieldInfo generationField = RequiredField(typeof(MusicAssetManager).BaseType, "_generation", typeof(int));
            MusicAssetManager.Receipt receipt = (MusicAssetManager.Receipt)receiptField.GetValue(null);
            int generation = (int)generationField.GetValue(null);
            if (receipt.Handle.Hash != 0 && receipt.Handle.Generation != generation)
                throw new InvalidOperationException("The outgoing native music receipt is already stale; expected generation " + generation
                    + ", actual " + receipt.Handle.Generation + ". Teardown must run before destruction.");

            int activeLoops = QuiesceOutgoingPlayWhileActive(scene);
            AudioPlaybackManager.StopAllCoroutines();
            // SafeInstance was proven present above. Original StopBgMusic retains
            // playlist/ambient behavior and must execute before scene destruction.
            if (MasterAudio.SoundsReady) AudioPlaybackManager.StopBgMusic();
            if (receipt.Handle.Hash != 0) manager.UnloadAsset(ref receipt);
            receiptField.SetValue(null, receipt);
            musicField.SetValue(null, null);
            playField.SetValue(null, null);
            endField.SetValue(null, null);
            UnityEngine.Debug.Log("OFFLINE MUSIC TEARDOWN: native playlist/ambient stopped and current route receipt released before "
                + scene.name + " unload; generation=" + generation + "; outgoing active loop components=" + activeLoops + ".");
        }

        private static int QuiesceOutgoingPlayWhileActive(Scene scene)
        {
            int stopped = 0;
            foreach (PlayWhileActive loop in UnityEngine.Object.FindObjectsOfType<PlayWhileActive>())
            {
                if (loop == null || !loop.isActiveAndEnabled || !loop.gameObject.scene.Equals(scene)) continue;
                // Original OnDisable stops its configured event. Run it while
                // the outgoing MasterAudio is alive; destruction then cannot
                // invoke this active behaviour after the Master disappears.
                loop.enabled = false;
                stopped++;
            }
            return stopped;
        }

        private static FieldInfo RequiredField(Type type, string name, Type fieldType)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != fieldType || !field.IsStatic)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
