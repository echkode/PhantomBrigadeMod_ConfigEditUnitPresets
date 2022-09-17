// Copyright (c) 2022 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using HarmonyLib;

namespace EchKode.PBMods.ConfigEditUnitPresets
{
	[HarmonyPatch]
	static class Patch
	{
		[HarmonyPatch(typeof(PhantomBrigade.Game.Systems.DataLinkerInitSystem), "Initialize")]
		[HarmonyPostfix]
		static void Dis_InitializePostfix()
		{
			UnitPresets.Update();
		}
	}
}
