using System.Linq;

using HarmonyLib;
using PhantomBrigade.Data;
using PhantomBrigade.Mods;

namespace EchKode.PBMods.ConfigEditUnitPresets
{
	using UnitPresetMultiLinker = DataMultiLinker<DataContainerUnitPreset>;

	static class UnitPresets
	{
		private const string unitPresetPathKey = "DataContainerUnitPreset";
		private const string modConfigEditsDirectoryPath = "ConfigEdits/";
		private const string modConfigOverrideDirectoryPath = "ConfigOverrides/";

		public static void Update()
		{
			ModifyUnitPresetPath();

			try
			{
				LoadAndProcessChanges();
			}
			finally
			{
				UnloadConfigChangeFiles();
				UnmodifyUnitPresetPath();
			}
		}

		private static void LoadAndProcessChanges()
		{
			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods standard unit preset count={UnitPresetMultiLinker.data.Count}");
			}

			var loadedData = ModManager.loadedModsLookup[ModLink.modId];
			ModManager.TryLoadingConfigOverrides(ModLink.modId, ModLink.modPath + modConfigOverrideDirectoryPath, loadedData);
			ModManager.TryLoadingConfigEdits(ModLink.modId, ModLink.modPath + modConfigEditsDirectoryPath, loadedData);
			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods reloaded config edits/overrides for {ModLink.modId}");
			}

			var dataTypeStatic = typeof(DataContainerUnitPreset);
			if (Harmony.DEBUG) { FileLog.Log($"!!! PBMods process config mods for dataTypeStatic={dataTypeStatic}"); }
			ModManagerFix.ProcessConfigModsForMultiLinker(dataTypeStatic, UnitPresetMultiLinker.data);

			foreach (var keyValuePair in UnitPresetMultiLinker.data)
			{
				if (Harmony.DEBUG) { FileLog.Log($"!!! PBMods after-serialization callback for key={keyValuePair.Key}"); }
				keyValuePair.Value.OnAfterDeserialization(keyValuePair.Key);
			}

			if (!DataMultiLinkerUtility.callbacksOnAfterDeserialization.ContainsKey(dataTypeStatic))
			{
				return;
			}

			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods call registered after-deserialization callbacks");
			}
			DataMultiLinkerUtility.callbacksOnAfterDeserialization[dataTypeStatic]?.Invoke();

			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods patched unit preset count={UnitPresetMultiLinker.data.Count}");
			}
		}

		private static void UnloadConfigChangeFiles()
		{
			// Clear out the config change files for UnitPresets after we've applied them so the system
			// doesn't try to re-apply them later on.

			try
			{
				var loadedData = ModManager.loadedModsLookup[ModLink.modId];
				var t = typeof(DataContainerUnitPreset);
				loadedData.configOverrides = loadedData.configOverrides
					.Where(m => m.type != t)
					.ToList();
				loadedData.configEdits = loadedData.configEdits
					.Where(m => m.type != t)
					.ToList();
			}
			catch (System.Exception ex)
			{
				if (Harmony.DEBUG)
				{
					FileLog.Log($"!!! PBMods failed to remove {ModLink.modId} config change files from ModManager: {ex}");
				}
			}
		}

		private static void ModifyUnitPresetPath()
		{
			var v = DataLinker<DataContainerPaths>.data.paths[unitPresetPathKey];
			if (v.EndsWith("/"))
			{
				return;
			}

			DataLinker<DataContainerPaths>.data.pathsInverted.Remove(v);
			v += "/";
			DataLinker<DataContainerPaths>.data.paths[unitPresetPathKey] = v;
			DataLinker<DataContainerPaths>.data.pathsInverted.Add(v, unitPresetPathKey);
			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods modified path for {unitPresetPathKey}");
			}
		}

		private static void UnmodifyUnitPresetPath()
		{
			var v = DataLinker<DataContainerPaths>.data.paths[unitPresetPathKey];
			if (!v.EndsWith("/"))
			{
				return;
			}

			DataLinker<DataContainerPaths>.data.pathsInverted.Remove(v);
			v = v.TrimEnd('/');
			DataLinker<DataContainerPaths>.data.paths[unitPresetPathKey] = v;
			DataLinker<DataContainerPaths>.data.pathsInverted.Add(v, unitPresetPathKey);
			if (Harmony.DEBUG)
			{
				FileLog.Log($"!!! PBMods unmodified path for {unitPresetPathKey}");
			}
		}
	}
}
