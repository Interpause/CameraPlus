﻿using System;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;
using IPA;
using IPA.Loader;
using IPA.Utilities;
using IPALogger = IPA.Logging.Logger;
using LogLevel = IPA.Logging.Logger.Level;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using System.IO;

namespace CameraPlus
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        private bool _init;
        private Harmony _harmony;

        public Action<Scene, Scene> ActiveSceneChanged;
        public ConcurrentDictionary<string, CameraPlusInstance> Cameras = new ConcurrentDictionary<string, CameraPlusInstance>();

        public static Plugin Instance { get; private set; }
        public static string Name => "CameraPlus";
        public static string MainCamera => "cameraplus";

        private RootConfig _rootConfig;
        private ProfileChanger _profileChanger;

        [Init]
        public void Init(IPALogger logger)
        {
            Logger.log = logger;
            Logger.Log("Logger prepared", LogLevel.Debug);
        }

        [OnStart]
        public void OnApplicationStart()
        {
            if (_init) return;
            _init = true;
            Instance = this;

            _harmony = new Harmony("com.brian91292.beatsaber.cameraplus");
            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to apply harmony patches! {ex}", LogLevel.Error);
            }

            SceneManager.activeSceneChanged += this.OnActiveSceneChanged;
            // Add our default cameraplus camera
            CameraUtilities.AddNewCamera(Plugin.MainCamera);
            CameraProfiles.CreateMainDirectory();

            string path = Path.Combine(UnityGame.UserDataPath, Plugin.Name + ".ini");
            _rootConfig = new RootConfig(path);
            _profileChanger = new ProfileChanger();

            Logger.Log($"{Plugin.Name} has started", LogLevel.Notice);
        }

        public void OnActiveSceneChanged(Scene from, Scene to)
        {
            SharedCoroutineStarter.instance.StartCoroutine(DelayedActiveSceneChanged(from, to));
        }

        private IEnumerator DelayedActiveSceneChanged(Scene from, Scene to)
        {
            yield return new WaitForSeconds(0.5f);
            // If any new cameras have been added to the config folder, render them
            // if(to.name == )

            CameraUtilities.ReloadCameras();

            if (ActiveSceneChanged != null)
            {
                if (_rootConfig.ProfileSceneChange)
                {
                    if (to.name == "GameCore") 
                    {
                        _profileChanger.ProfileChange(_rootConfig.GameProfile);
                    }
                    else if (to.name == "MenuCore")
                        _profileChanger.ProfileChange(_rootConfig.MenuProfile);
                }

                yield return new WaitForSeconds(1.0f);

                // Invoke each activeSceneChanged event
                foreach (var func in ActiveSceneChanged?.GetInvocationList())
                {
                    try
                    {
                        func?.DynamicInvoke(from, to);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Exception while invoking ActiveSceneChanged:" +
                            $" {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
                    }
                }
            }
            if (to.name == "GameCore")
                CameraUtilities.SetAllCameraCulling();
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            _harmony.UnpatchAll("com.brian91292.beatsaber.cameraplus");
        }

        public void OnSceneLoaded(Scene scene, LoadSceneMode sceneMode) { }
        public void OnSceneUnloaded(Scene scene) { }
        public void OnUpdate() { }

        public void OnFixedUpdate()
        {
            // Fix the cursor when the user resizes the main camera to be smaller than the canvas size and they hover over the black portion of the canvas
            if (CameraPlusBehaviour.currentCursor != CameraPlusBehaviour.CursorType.None && !CameraPlusBehaviour.anyInstanceBusy && 
                CameraPlusBehaviour.wasWithinBorder && CameraPlusBehaviour.GetTopmostInstanceAtCursorPos() == null)
            {
                CameraPlusBehaviour.SetCursor(CameraPlusBehaviour.CursorType.None);
                CameraPlusBehaviour.wasWithinBorder = false;
            }
        }
    }
}
