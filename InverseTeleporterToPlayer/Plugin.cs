using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;


namespace InverseTeleporterToPlayer
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public Harmony Patcher { get; private set; }
        public AssetBundle MainAssetBundle { get; private set; }
        public ManualLogSource Log { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            Patcher = new Harmony(PluginInfo.PLUGIN_GUID);
            Patcher.PatchAll();
            MainAssetBundle = AssetBundle.LoadFromMemory(InverseTeleporterToPlayer.Properties.Resources.brazil);
            Log = Logger;
            Logger.LogInfo($"{MainAssetBundle}");
            NetcodeWeaver();
        }

        private static void NetcodeWeaver()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }
}