﻿using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using UnityEngine;

namespace UMM.Loader
{
    public static class UltraModManager
    {
        public static Dictionary<string, ModInformation> foundMods = new Dictionary<string, ModInformation>();
        public static Dictionary<string, ModInformation> allLoadedMods = new Dictionary<string, ModInformation>();
        public static bool outdated = false;
        public static string newLoaderVersion = "";
        private static bool initialized = false;
        private static Dictionary<ModInformation, GameObject> modObjects = new Dictionary<ModInformation, GameObject>();

        internal static void InitializeManager()
        {
            if (!initialized)
            {
                Debug.Log("Beginning UltraModManager");
                initialized = true;
                CollectAssemblies();
                LoadOnStart();
            }
        }

        private static void CollectAssemblies()
        {
            DirectoryInfo modsDirectory = new DirectoryInfo(Environment.CurrentDirectory + @"\BepInEx\UMM Mods\");
            if (modsDirectory.Exists)
                foreach (FileInfo info in modsDirectory.GetFiles("*.dll", SearchOption.AllDirectories))
                    LoadFromAssembly(info);
            else
                Directory.CreateDirectory(Environment.CurrentDirectory + @"\BepInEx\UMM Mods\");
            Debug.Log("Found " + foundMods.Count + " mods that can be loaded.");
        }

        private static void LoadOnStart()
        {
            int loadedMods = 0;
            foreach (ModInformation info in foundMods.Values)
            {
                if (info.loadOnStart)
                {
                    info.LoadThisMod();
                    loadedMods++;
                }
            }
            Debug.Log("Loaded " + loadedMods + " mods on start");
        }

        public static void LoadFromAssembly(FileInfo fInfo)
        {
            try
            {
                Assembly ass = Assembly.LoadFile(fInfo.FullName);
                foreach (Type type in ass.GetTypes())
                {
                    ModInformation info;
                    if (type.IsSubclassOf(typeof(UKMod)))
                        info = new ModInformation(type, ModInformation.ModType.UKMod);
                    else if (type.IsSubclassOf(typeof(BaseUnityPlugin)))
                        info = new ModInformation(type, ModInformation.ModType.BepInPlugin);
                    else
                        continue;
                    Debug.Log("Adding mod info " + fInfo.FullName + " " + type.Name);
                    foundMods.Add(info.GUID, info);
                    object retrievedData = UKAPI.SaveFileHandler.RetrieveModData("LoadOnStart", info.modName);
                    if (retrievedData != null && bool.Parse(retrievedData.ToString()))
                        info.loadOnStart = true;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Caught exception while trying to load assembly " + fInfo.FullName);
                Debug.Log(e.ToString());
                return;
            }
        }

        internal static BepInPlugin GetBepinMetaData(Type t)
        {
            object[] customAttributes = t.GetCustomAttributes(typeof(BepInPlugin), true);
            if (customAttributes.Length == 0)
            {
                throw new NullReferenceException("Could not find the metadata (BepInPlugin) to BaseUnityPlugin " + t.FullName);
            }
            return (BepInPlugin)customAttributes[0];
        }

        internal static UKPlugin GetUKMetaData(Type t)
        {
            object[] customAttributes = t.GetCustomAttributes(typeof(UKPlugin), true);
            if (customAttributes.Length == 0)
            {
                throw new NullReferenceException("Could not find the metadata (UKPlugin) to UKMod " + t.FullName);
            }
            return (UKPlugin)customAttributes[0];
        }

        internal static Dependency[] GetBepinDependencies(Type t)
        {
            BepInDependency[] customAttributes = (BepInDependency[])t.GetCustomAttributes(typeof(BepInDependency), true);
            List<Dependency> dependencies = new List<Dependency>();
            foreach (BepInDependency attribute in customAttributes)
            {
                dependencies.Add(new Dependency() { GUID = attribute.DependencyGUID, MinimumVersion = attribute.MinimumVersion });
            }
            return dependencies.ToArray();
        }

        internal static Dependency[] GetUKModDependencies(Type t)
        {
            UKDependency[] customAttributes = (UKDependency[])t.GetCustomAttributes(typeof(UKDependency), true);
            List<Dependency> dependencies = new List<Dependency>();
            foreach (UKDependency attribute in customAttributes)
            {
                dependencies.Add(new Dependency() { GUID = attribute.GUID, MinimumVersion = attribute.MinimumVersion });
            }
            return dependencies.ToArray();
        }

        public static void LoadMod(ModInformation info)
        {
            if (allLoadedMods.ContainsKey(info.GUID)) return;
            foreach (Dependency dependency in info.dependencies)
            {
                if (foundMods.ContainsKey(dependency.GUID))
                {
                    if (foundMods[dependency.GUID].modVersion >= dependency.MinimumVersion)
                    {
                        LoadMod(foundMods[dependency.GUID]);
                    }
                    else
                    {
                        info.loaded = false;
                        Debug.LogError($"Required dependency ({foundMods[dependency.GUID].modName}, version {foundMods[dependency.GUID].modVersion}) did not meet version requirements of {info.modName} (minimum version {dependency.MinimumVersion})");
                        return;
                    }
                }
                else
                {
                    info.loaded = false;
                    Debug.LogError($"Required dependency ({dependency.GUID}) of {info.modName} not found.");
                    return;
                }
            }
            GameObject modObject = GameObject.Instantiate(new GameObject());
            UKMod newMod = null;
            try
            {
                Debug.Log("Trying to load mod " + info.modName);
                if (info.mod.IsSubclassOf(typeof(BaseUnityPlugin)))
                {
                    GameObject.DontDestroyOnLoad(modObject);
                    modObject.SetActive(false);
                    modObject.AddComponent(info.mod);
                    allLoadedMods.Add(info.GUID, info);
                    modObject.SetActive(true);
                    Debug.Log("Loaded BepInExPlugin " + info.modName);
                    return;
                }
                if (!info.mod.IsSubclassOf(typeof(UKMod)))
                    throw new ArgumentException("LoadMod(Type mod) was called using a type that did not inherit from UKMod or BaseUnityPlugin, type name is " + info.mod.Name);
                GameObject.DontDestroyOnLoad(modObject);
                modObject.SetActive(false);
                newMod = modObject.AddComponent(info.mod) as UKMod;
                allLoadedMods.Add(info.GUID, info);
                modObjects.Add(info, modObject);
                UKPlugin metaData = UltraModManager.GetUKMetaData(info.mod);
                if (!metaData.allowCyberGrindSubmission)
                    UKAPI.DisableCyberGrindSubmission(info.modName);
                modObject.SetActive(true);
                newMod.OnModLoaded();
                Debug.Log("Loaded UKMod " + info.modName);
            }
            catch (Exception e)
            {
                Debug.LogError("Caught exception while trying to load modinformation " + info.modName);
                Debug.LogException(e);
                if (modObject != null)
                {
                    if (newMod != null)
                        newMod.OnModUnload();
                    GameObject.Destroy(modObject); // I don't know if this is a good thing to do, if not please scream at me to remove it
                }
            }
        }

        public static void UnloadMod(ModInformation info)
        {
            if (modObjects.ContainsKey(info) && info.supportsUnloading)
            {
                Debug.Log("trying to unload mod " + info.modName);
                GameObject modObject = modObjects[info];
                UKMod mod = modObject.GetComponent<UKMod>();
                mod.OnModUnloaded.Invoke();
                mod.OnModUnload();
                modObjects.Remove(info);
                allLoadedMods.Remove(info.GUID);
                GameObject.Destroy(modObject);
                if (!UltraModManager.GetUKMetaData(info.mod).allowCyberGrindSubmission)
                    UKAPI.RemoveDisableCyberGrindReason(info.modName);
                Debug.Log("successfully unloaded mod " + info.modName);
            }
        }
    }
}