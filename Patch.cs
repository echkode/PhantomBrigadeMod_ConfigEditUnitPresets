﻿using HarmonyLib;

namespace EchKode.PBMods.ConfigEditUnitPresets
{
	[HarmonyPatch]
	static class Patch
	{
		private static bool patched;

		[HarmonyPatch(typeof(PhantomBrigade.GameController))]
		[HarmonyPatch("ProcessRequests", MethodType.Normal)]
		[HarmonyPrefix]
		static void Gc_ProcessRequestsPrefix()
		{
			if (patched)
			{
				return;
			}

			UnitPresets.Update();
			patched = true;
		}
	}
}