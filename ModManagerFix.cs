// Copyright (c) 2022 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using PhantomBrigade.Data;
using PhantomBrigade.Mods;
using UnityEngine;

namespace EchKode.PBMods.ConfigEditUnitPresets
{
	static class ModManagerFix
	{
		private enum EditOperation
		{
			Overwrite = 0,
			Insert,
			Remove,
			DefaultValue,
		}

		private class EditSpec
		{
			public string dataTypeName;
			public object root;
			public string filename;
			public string fieldPath;
			public string valueRaw;
			public int i;
			public string modID;
			public EditState state = new EditState();
		}

		private class EditState
		{
			public object target;
			public EditOperation op;
			public int pathSegmentCount;
			public int pathSegmentIndex;
			public string pathSegment;
			public bool atEndOfPath;
			public int targetIndex;
			public object parent;
			public string targetKey;
			public FieldInfo fieldInfo;
			public Type targetType;
		}

		private static class Constants
		{
			public static class Operator
			{
				public const string Insert = "!+";
				public const string Remove = "!-";
				public const string DefaultValue = "!d";
			}
		}

		private static Type typeString;
		private static Type typeBool;
		private static Type typeInt;
		private static Type typeFloat;
		private static Type typeVector2;
		private static Type typeVector3;
		private static Type typeVector4;
		private static Type typeIList;
		private static Type typeHashSet;
		private static Type typeIDictionary;

		private static Dictionary<string, EditOperation> operationMap;
		private static HashSet<EditOperation> allowedHashSetOperations;
		private static Dictionary<Type, Action<EditSpec, Action<object>>> updaterMap;
		private static Dictionary<string, Type> tagTypeMap;

		internal static void Initialize()
		{
			typeString = typeof(string);
			typeBool = typeof(bool);
			typeInt = typeof(int);
			typeFloat = typeof(float);
			typeVector2 = typeof(Vector2);
			typeVector3 = typeof(Vector3);
			typeVector4 = typeof(Vector4);
			typeIList = typeof(IList);
			typeHashSet = typeof(HashSet<string>);
			typeIDictionary = typeof(IDictionary);

			operationMap = new Dictionary<string, EditOperation>()
			{
				[Constants.Operator.Insert] = EditOperation.Insert,
				[Constants.Operator.Remove] = EditOperation.Remove,
				[Constants.Operator.DefaultValue] = EditOperation.DefaultValue,
			};

			allowedHashSetOperations = new HashSet<EditOperation>()
			{
				EditOperation.Insert,
				EditOperation.Remove,
			};

			updaterMap = new Dictionary<Type, Action<EditSpec, Action<object>>>()
			{
				[typeString] = UpdateStringField,
				[typeBool] = UpdateBoolField,
				[typeInt] = UpdateIntField,
				[typeFloat] = UpdateFloatField,
				[typeVector2] = UpdateVector2Field,
				[typeVector3] = UpdateVector3Field,
				[typeVector4] = UpdateVector4Field,
				[typeHashSet] = UpdateHashSet,
			};

			tagTypeMap = new Dictionary<string, Type>()
			{
				["PartResolverClear"] = typeof(DataBlockPartSlotResolverClear),
			};
		}

		internal static void ProcessConfigModsForMultiLinker<T>(
		  Type dataType,
		  SortedDictionary<string, T> dataInternal)
		  where T : DataContainer, new()
		{
			if (dataInternal == null)
			{
				return;
			}
			if (dataType == null)
			{
				return;
			}
			if (ModManager.config == null)
			{
				return;
			}
			if (!ModManager.config.enabled)
			{
				return;
			}
			if (ModManager.loadedMods == null)
			{
				return;
			}

			var spec = new EditSpec()
			{
				dataTypeName = dataType.Name,
			};

			ModManager.loadedMods
				.Select((m, i) => new
				{
					Index = i,
					LoadedMod = m,
				})
				.Where(mi => mi.LoadedMod != null)
				.Where(mi => mi.LoadedMod.metadata != null)
				.ToList()
				.ForEach(mi =>
				{
					spec.i = mi.Index;
					var loadedMod = mi.LoadedMod;
					spec.modID = loadedMod.metadata.id;

					if (loadedMod.metadata.includesConfigOverrides && loadedMod.configOverrides.Any())
					{
						ApplyOverrides(spec, loadedMod, dataType, dataInternal);
					}

					if (loadedMod.metadata.includesConfigEdits && loadedMod.configEdits.Any())
					{
						ApplyEdits(spec, loadedMod, dataType, dataInternal);
					}
				});
		}

		private static void ApplyOverrides<T>(
			EditSpec spec,
			ModLoadedData loadedMod,
			Type dataType,
			SortedDictionary<string, T> dataInternal)
		  where T : DataContainer, new()
		{
			loadedMod.configOverrides
				.Where(o => o != null)
				.Where(o => o.containerObject != null)
				.Where(o => o.type == dataType)
				.ToList()
				.ForEach(configOverride =>
				{
					if (configOverride.containerObject is T containerObject)
					{
						var key = configOverride.key;
						if (dataInternal.ContainsKey(key))
						{
							Debug.LogWarning($"Mod {spec.i} ({spec.modID}) replaces config {key} of type {spec.dataTypeName}");
							dataInternal[key] = containerObject;
						}
						else
						{
							Debug.LogWarning($"Mod {spec.i} ({spec.modID}) injects additional config {key} of type {spec.dataTypeName}");
							dataInternal.Add(key, containerObject);
						}
					}
				});
		}

		private static void ApplyEdits<T>(
			EditSpec spec,
			ModLoadedData loadedMod,
			Type dataType,
			SortedDictionary<string, T> dataInternal)
		  where T : DataContainer, new()
		{
			var editsCount = loadedMod.configEdits.Count;
			loadedMod.configEdits
				.Select((e, i) => new
				{
					Index = i,
					Edit = e,
				})
				.Where(ei => ei.Edit != null)
				.Where(ei => ei.Edit.data != null)
				.Where(ei => ei.Edit.type == dataType)
				.ToList()
				.ForEach(ei =>
				{
					Debug.LogWarningFormat(
						"Mod {0} ({1}) applying edit script {2} of {3}: <key={4};type={5};filename={6}>",
						spec.i,
						spec.modID,
						ei.Index + 1,
						editsCount,
						ei.Edit.key,
						ei.Edit.typeName,
						ei.Edit.filename);
					ApplyEdit(spec, dataInternal, ei.Edit);
				});
		}

		private static void ApplyEdit<T>(
			EditSpec spec,
			SortedDictionary<string, T> dataInternal,
			ModConfigEditLoaded configEdit)
		  where T : DataContainer, new()
		{
			var key = configEdit.key;
			if (!dataInternal.ContainsKey(key))
			{
				Debug.LogWarning($"Mod {spec.i} ({spec.modID}) attempts to edit config {key} of type {spec.dataTypeName}, which doesn't exist");
				return;
			}

			var data = configEdit.data;
			if (data.removed)
			{
				Debug.LogWarning($"Mod {spec.i} ({spec.modID}) removes config {key} of type {spec.dataTypeName}");
				dataInternal.Remove(key);
				return;
			}

			if (data.edits == null)
			{
				return;
			}

			if (!data.edits.Any())
			{
				return;
			}

			Debug.LogWarning($"Mod {spec.i} ({spec.modID}) edits config {key} of type {spec.dataTypeName}");
			var kvp = dataInternal[key];
			spec.root = kvp;
			spec.filename = kvp.key;
			var editsCount = data.edits.Count;
			data.edits
				.Select((e, i) => new
				{
					Index = i,
					Edit = e,
				})
				.ToList()
				.ForEach(ei =>
				{
					Debug.LogWarningFormat(
						"Mod {0} ({1}) applying edit {2} of {3} to config {4}",
						spec.i,
						spec.modID,
						ei.Index + 1,
						editsCount,
						key);
					spec.fieldPath = ei.Edit.path;
					spec.valueRaw = ei.Edit.value;
					ProcessFieldEdit(spec);
				});
		}

		private static void ProcessFieldEdit(EditSpec spec)
		{
			if (string.IsNullOrEmpty(spec.fieldPath) || string.IsNullOrEmpty(spec.valueRaw))
			{
				return;
			}

			var (eop, valueRaw) = ParseOperation(spec.valueRaw);
			spec.state.op = eop;
			spec.valueRaw = valueRaw;

			spec.state.target = spec.root;
			spec.state.parent = spec.root;
			spec.state.targetIndex = -1;
			spec.state.targetKey = null;
			spec.state.fieldInfo = null;
			spec.state.targetType = null;

			if (!WalkFieldPath(spec))
			{
				return;
			}

			var (ok, update) = ValidateEditState(spec);
			if (!ok)
			{
				return;
			}

			if (updaterMap.TryGetValue(spec.state.targetType, out var updater))
			{
				updater(spec, update);
				return;
			}

			if (spec.state.op != EditOperation.DefaultValue)
			{
				Report(
					spec,
					"attempts to edit",
					$"Value type {spec.state.targetType.Name} has no string parsing implementation - try using {Constants.Operator.DefaultValue} keyword if you're after filling it with default instance");
				return;
			}

			if (spec.state.fieldInfo == null)
			{
				var parentType = spec.state.parent?.GetType().Name ?? "null";
				var targetType = spec.state.target?.GetType().Name ?? "null";
				Report(
					spec,
					"attempts to edit",
					$"WalkFieldPath() failed to terminate properly <segment={spec.state.pathSegment};segmentIndex={spec.state.pathSegmentIndex};segmentCount={spec.state.pathSegmentCount};atEnd={spec.state.atEndOfPath};op={spec.state.op};parent={parentType};target={targetType};targetType={spec.state.targetType};targetIndex={spec.state.targetIndex};targetKey={spec.state.targetKey}>");
				return;
			}

			var instanceType = spec.state.targetType;
			if (valueRaw.StartsWith("!"))
			{
				if (!tagTypeMap.TryGetValue(valueRaw.Substring(1), out instanceType))
				{
					Report(
						spec,
						"attempts to edit",
						$"There is no type associated with tag {valueRaw}");
					return;
				}
			}

			var instance = Activator.CreateInstance(instanceType);
			spec.state.fieldInfo.SetValue(spec.state.parent, instance);
			Report(
				spec,
				"edits",
				$"Assigning new default object of type {instanceType.Name} to target field");
		}

		private static (EditOperation, string) ParseOperation(string valueRaw)
		{
			foreach (var kvp in operationMap)
			{
				var opr = kvp.Key;
				if (valueRaw.EndsWith(opr))
				{
					return (kvp.Value, valueRaw.Replace(opr, "").TrimEnd(' '));
				}
			}
			return (EditOperation.Overwrite, valueRaw);
		}

		private static bool WalkFieldPath(EditSpec spec)
		{
			var pathSegments = spec.fieldPath.Split('.');
			spec.state.pathSegmentCount = pathSegments.Length;

			for (var i = 0; i < pathSegments.Length; i += 1)
			{
				spec.state.pathSegmentIndex = i;
				spec.state.pathSegment = pathSegments[i];
				spec.state.atEndOfPath = spec.state.pathSegmentIndex == spec.state.pathSegmentCount - 1;

				if (spec.state.target == null)
				{
					Report(
						spec,
						"attempts to edit",
						$"Can't proceed past {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}), current target reference is null");
					return false;
				}

				spec.state.targetType = spec.state.target.GetType();
				var child = i > 0;
				if (child && typeIList.IsAssignableFrom(spec.state.targetType))
				{
					if (!ProduceListElement(spec))
					{
						return false;
					}
				}
				else if (child && typeIDictionary.IsAssignableFrom(spec.state.targetType))
				{
					if (!ProduceMapEntry(spec))
					{
						return false;
					}
				}
				else if (!ProduceField(spec))
				{
					return false;
				}
			}

			return true;
		}

		private static bool ProduceListElement(EditSpec spec)
		{
			var list = spec.state.target as IList;
			if (!int.TryParse(spec.state.pathSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < 0)
			{
				Report(
					spec,
					"attempts to edit",
					$"Index {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}) can't be parsed or is negative");
				return false;
			}

			var elementType = list.GetType().GetGenericArguments()[0];
			if (spec.state.atEndOfPath)
			{
				if (!EditList(spec, list, result, elementType))
				{
					return false;
				}
			}
			else if (result >= list.Count)
			{
				Report(
					spec,
					"attempts to edit",
					$"Can't proceed past {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}), current target reference is beyond end of list (size={list.Count})");
				return false;
			}

			spec.state.parent = spec.state.target;
			spec.state.fieldInfo = null;
			spec.state.targetIndex = result;
			spec.state.targetKey = null;
			spec.state.target = list[result];
			spec.state.targetType = elementType;

			return true;
		}

		private static bool EditList(
			EditSpec spec,
			IList list,
			int index,
			Type elementType)
		{
			var outOfBounds = index >= list.Count;

			if (spec.state.op == EditOperation.Insert)
			{
				var instance = Activator.CreateInstance(elementType);
				if (outOfBounds)
				{
					list.Add(instance);
					Report(
						spec,
						"edits",
						$"Adding new entry of type {elementType.Name} to end of the list (step {spec.state.pathSegmentIndex})");
				}
				else
				{
					list.Insert(index, instance);
					Report(
						spec,
						"edits",
						$"Inserting new entry of type {elementType.Name} to index {index} of the list (step {spec.state.pathSegmentIndex})");
				}

				return !string.IsNullOrWhiteSpace(spec.valueRaw);
			}

			if (spec.state.op == EditOperation.Remove)
			{
				if (outOfBounds)
				{
					Report(
						spec,
						"attempts to edit",
						$"Index {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}) can't be removed as it's out of bounds for list size {list.Count}");
					return false;
				}

				list.RemoveAt(index);
				Report(
					spec,
					"edits",
					$"Removing entry at index {index} of the list (step {spec.state.pathSegmentIndex})");
				return false;
			}

			return true;
		}

		private static bool ProduceMapEntry(EditSpec spec)
		{
			var map = spec.state.target as IDictionary;
			var key = spec.state.pathSegment;
			var entryExists = map.Contains(key);

			var entryTypes = map.GetType().GetGenericArguments();
			var keyType = entryTypes[0];
			var valueType = entryTypes[1];

			if (spec.state.atEndOfPath)
			{
				if (!EditMap(
					spec,
					map,
					keyType,
					valueType,
					key,
					entryExists))
				{
					return false;
				}
			}
			else if (!entryExists)
			{
				Report(
					spec,
					"attempts to edit",
					$"Can't proceed past {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}), current target reference doesn't exist in dictionary)");
				return false;
			}

			spec.state.parent = spec.state.target;
			spec.state.fieldInfo = null;
			spec.state.targetIndex = -1;
			spec.state.targetKey = key;
			spec.state.target = map[key];
			spec.state.targetType = valueType;

			return true;
		}

		private static bool EditMap(
			EditSpec spec,
			IDictionary map,
			Type keyType,
			Type valueType,
			object key,
			bool entryExists)
		{
			if (spec.state.op == EditOperation.Insert)
			{
				if (!entryExists)
				{
					if (keyType != typeString)
					{
						Report(
							spec,
							"attempts to edit",
							$"Adding new dictionary entry of type {valueType.Name} to key {key} (step {spec.state.pathSegmentIndex}) - only string keys are supported");
						return false;
					}

					object instance = Activator.CreateInstance(valueType);
					map.Add(key, instance);
					Report(
						spec,
						"edits",
						$"Adding key {key} (step {spec.state.pathSegmentIndex}) to target dictionary");
				}
				else
				{
					Report(
						spec,
						"attempts to edit",
						$"Key {key} already exists, ignoring the command to add it");
				}

				return !string.IsNullOrWhiteSpace(spec.valueRaw);
			}

			if (spec.state.op == EditOperation.Remove)
			{
				if (!entryExists)
				{
					Report(
						spec,
						"attempts to edit",
						$"Key {key} (step {spec.state.pathSegmentIndex}) can't be removed from target dictionary - it can't be found");
					return false;
				}

				Report(
					spec,
					"edits",
					$"Removing key {key} (step {spec.state.pathSegmentIndex}) from target dictionary");
				map.Remove(key);
				return false;
			}

			return true;
		}

		private static bool ProduceField(EditSpec spec)
		{
			var field = spec.state.targetType.GetField(spec.state.pathSegment);
			if (field == null)
			{
				Report(
					spec,
					"attempts to edit",
					$"Field {spec.state.pathSegment} (step {spec.state.pathSegmentIndex}) could not be found on type {spec.state.targetType}");
				return false;
			}

			spec.state.parent = spec.state.target;
			spec.state.fieldInfo = field;
			spec.state.targetIndex = -1;
			spec.state.targetKey = null;
			spec.state.target = field.GetValue(spec.state.target);
			spec.state.targetType = field.FieldType;

			return true;
		}

		private static (bool, Action<object>) ValidateEditState(EditSpec spec)
		{
			var parentType = spec.state.parent.GetType();
			var parentIsList = typeIList.IsAssignableFrom(parentType);
			if (parentIsList)
			{
				if (spec.state.targetIndex == -1)
				{
					Report(
						spec,
						"attempts to edit",
						$"Value is contained in a list but list index {spec.state.pathSegment} is not valid");
					return (false, null);
				}

				var parentList = (IList)spec.state.parent;
				var targetIndex = spec.state.targetIndex;
				return (true, v => parentList[targetIndex] = v);
			}

			var parentIsMap = typeIDictionary.IsAssignableFrom(parentType);
			if (parentIsMap)
			{
				if (spec.state.targetKey == null)
				{
					Report(
						spec,
						"attempts to edit",
						$"Value is contained in a dictionary but the key {spec.state.pathSegment} is not valid");
					return (false, null);
				}

				var parentMap = (IDictionary)spec.state.parent;
				var targetKey = spec.state.targetKey;
				return (true, v => parentMap[targetKey] = v);
			}

			if (spec.state.fieldInfo == null)
			{
				Report(
					spec,
					"attempts to edit",
					$"Value can't be modified due to missing field info");
				return (false, null);
			}

			var parent = spec.state.parent;
			var fieldInfo = spec.state.fieldInfo;
			return (true, v => fieldInfo.SetValue(parent, v));
		}

		private static void UpdateStringField(EditSpec spec, Action<object> update)
		{
			var v = spec.state.op != EditOperation.DefaultValue ? spec.valueRaw : null;
			update(v);
			Report(
				spec,
				"edits",
				$"String field modified with value {v}");
		}

		private static void UpdateBoolField(EditSpec spec, Action<object> update)
		{
			var v = spec.state.op != EditOperation.DefaultValue
				&& string.Equals(spec.valueRaw, "true", StringComparison.OrdinalIgnoreCase);
			update(v);
			Report(
				spec,
				"edits",
				$"Bool field modified with value {v}");
		}

		private static void UpdateIntField(EditSpec spec, Action<object> update)
		{
			var v = 0;
			if (spec.state.op != EditOperation.DefaultValue)
			{
				if (!int.TryParse(spec.valueRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
				{
					Report(
						spec,
						"attempts to edits",
						$"Integer field can't be overwritten - can't parse raw value {spec.valueRaw}");
					return;
				}
			}

			update(v);
			Report(
				spec,
				"edits",
				$"Integer field modified with value {v}");
		}

		private static void UpdateFloatField(EditSpec spec, Action<object> update)
		{
			var v = 0.0f;
			if (spec.state.op != EditOperation.DefaultValue)
			{
				if (!float.TryParse(spec.valueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
				{
					Report(
						spec,
						"attempts to edits",
						$"Float field can't be overwritten - can't parse raw value {spec.valueRaw}");
					return;
				}
			}

			update(v);
			Report(
				spec,
				"edits",
				$"Float field modified with value {v}");
		}

		private static void UpdateVector2Field(EditSpec spec, Action<object> update)
		{
			UpdateVectorField(
				spec,
				update,
				2,
				ary => new Vector2(ary[0], ary[1]),
				Vector2.zero);
		}

		private static void UpdateVector3Field(EditSpec spec, Action<object> update)
		{
			UpdateVectorField(
				spec,
				update,
				3,
				ary => new Vector3(ary[0], ary[1], ary[2]),
				Vector3.zero);
		}

		private static void UpdateVector4Field(EditSpec spec, Action<object> update)
		{
			UpdateVectorField(
				spec,
				update,
				4,
				ary => new Vector4(ary[0], ary[1], ary[2], ary[3]),
				Vector4.zero);
		}

		private static void UpdateVectorField(
			EditSpec spec,
			Action<object> update,
			int vectorLength,
			Func<float[], object> ctor,
			object zero)
		{
			var v = zero;
			if (spec.state.op != EditOperation.DefaultValue)
			{
				var (ok, parsed) = ParseVectorValue(spec, vectorLength, ctor);
				if (!ok)
				{
					return;
				}
				v = parsed;
			}

			update(v);
			Report(
				spec,
				"edits",
				$"Vector{vectorLength} field modified with value {v}");
		}

		private static (bool, object) ParseVectorValue(
			EditSpec spec,
			int vectorLength,
			Func<float[], object> ctor)
		{
			if (!spec.valueRaw.StartsWith("(") || !spec.valueRaw.EndsWith(")"))
			{
				Report(
					spec,
					"attempts to edits",
					$"Vector{vectorLength} field can't be overwritten - can't parse raw value {spec.valueRaw} - missing parentheses");
				return (false, null);
			}

			var valueRaw = spec.valueRaw.Substring(1, spec.valueRaw.Length - 2);
			var velems = valueRaw.Split(',');
			if (velems.Length != vectorLength)
			{
				Report(
					spec,
					"attempts to edits",
					$"Vector{vectorLength} field can't be overwritten - can't parse raw value {spec.valueRaw} - invalid number of elements");
				return (false, null);
			}

			var parsed = new float[velems.Length];
			for (var i = 0; i < velems.Length; i += 1)
			{
				if (!float.TryParse(velems[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
				{
					Report(
						spec,
						"attempts to edits",
						$"Vector{vectorLength} field can't be overwritten - can't parse raw value {spec.valueRaw}");
					return (false, null);
				}
				parsed[i] = result;
			}

			return (true, ctor(parsed));
		}

		private static void UpdateHashSet(EditSpec spec, Action<object> _)
		{
			if (!allowedHashSetOperations.Contains(spec.state.op))
			{
				Report(
					spec,
					"attempts to edit",
					"No addition or removal keywords detected - no other operations are supported on hashsets");
				return;
			}

			var stringSet = spec.state.target as HashSet<string>;
			var found = stringSet.Contains(spec.valueRaw);

			switch (spec.state.op)
			{
				case EditOperation.Insert:
					if (found)
					{
						Report(
							spec,
							"attempts to edit",
							$"Value {spec.valueRaw} already exists in target set, ignoring addition command prompted by {Constants.Operator.Insert} keyword");
						return;
					}
					stringSet.Add(spec.valueRaw);
					Report(
						spec,
						"edits",
						$"Value {spec.valueRaw} is added to target set due to {Constants.Operator.Insert} keyword");
					break;
				case EditOperation.Remove:
					if (!found)
					{
						Report(
							spec,
							"attempts to edit",
							$"Value {spec.valueRaw} doesn't exist in target set, ignoring removal command prompted by {Constants.Operator.Remove} keyword");
						return;
					}
					stringSet.Remove(spec.valueRaw);
					Report(
						spec,
						"edits",
						$"Value {spec.valueRaw} is removed from target set due to {Constants.Operator.Remove} keyword");
					break;
			}
		}

		private static void Report(EditSpec spec, string verb, string msg)
		{
			Debug.LogWarning($"Mod {spec.i} ({spec.modID}) {verb} config {spec.filename} of type {spec.dataTypeName}, field {spec.fieldPath} | {msg}");
		}
	}
}

