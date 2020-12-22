using HarmonyLib;
using Jobs;
using Pipliz.JSON;
using Science;
using System;
using System.Collections.Generic;

namespace grasmanek94.BiomeBackPropagation
{
    [HarmonyPatch(typeof(CommandToolManager))]
    [HarmonyPatch("IsInScienceBiome")]
    [HarmonyPatch(new Type[] { typeof(Players.Player), typeof(string) })]
    class CommandToolManagerHookIsInScienceBiome
    {
        static bool Prefix(CommandToolManager __instance, ref bool __result, Players.Player p, string biome)
        {
            if (p.ActiveColony == null || 
                p.ActiveColony.Banners == null || 
                p.ActiveColony.Banners.Length == 0 || 
                ServerManager.ScienceManager == null ||
                biome == null)
            {
                return true;
            }

            ScienceKey key_raw = ServerManager.ScienceManager.GetKey(biome.Replace("sciencebiome.", "biome."));
            ScienceKey key_science = ServerManager.ScienceManager.GetKey(biome);

            if (!BiomeBackPropagation.HasScienceBiome(p.ActiveColony, key_raw) && 
                !BiomeBackPropagation.HasScienceBiome(p.ActiveColony, key_science))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [ModLoader.ModManager]
    public static class BiomeBackPropagation
    {
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnAssemblyLoaded, "grasmanek94.BiomeBackPropagation.OnAssemblyLoaded")]
        static void OnAssemblyLoaded(string assemblyPath)
        {
            var harmony = new Harmony("grasmanek94.BiomeBackPropagation");
            harmony.PatchAll();
        }

        static List<ScienceKey> sciencebiomes;

        public static readonly List<string> biomes = new List<string>
        {
            "biome.oldworld",
            "biome.newworld",
            "biome.fareast",
            "biome.tropics",
            "biome.arctic",
            "sciencebiome.oldworld",
            "sciencebiome.newworld",
            "sciencebiome.fareast",
            "sciencebiome.tropics",
            "sciencebiome.arctic"
        };

        static void Initialize()
        {
            if (sciencebiomes != null && sciencebiomes.Count > 0)
            {
                return;
            }

            sciencebiomes = new List<ScienceKey>();

            if (ServerManager.ScienceManager == null)
            {
                return;
            }

            foreach (var biome in biomes)
            {
                ScienceKey key = ServerManager.ScienceManager.GetKey(biome);
                if (key.Researchable != null)
                {
                    sciencebiomes.Add(key);
                }
            }
        }

        static List<Colony> GetAllColonies(Colony start)
        {
            List<Colony> list = new List<Colony>();

            if (start == null)
            {
                return list;
            }

            // bool = isProcessed
            Dictionary<Colony, bool> colonies = new Dictionary<Colony, bool>();
            Dictionary<Players.Player, bool> players = new Dictionary<Players.Player, bool>();

            colonies.Add(start, false);

            while (colonies.ContainsValue(false) || players.ContainsValue(false))
            {
                var loop_colonies = new List<Colony>(colonies.Keys);
                foreach (var colony in loop_colonies)
                {
                    if (colony == null)
                    {
                        continue;
                    }

                    if (!colonies[colony])
                    {
                        list.Add(colony);
                        colonies[colony] = true;
                        foreach (var owner in colony.Owners)
                        {
                            if (!players.ContainsKey(owner))
                            {
                                players.Add(owner, false);
                            }
                        }
                    }
                }

                var loop_players = new List<Players.Player>(players.Keys);
                foreach (var player in loop_players)
                {
                    if (player == null)
                    {
                        continue;
                    }

                    if (!players[player])
                    {
                        players[player] = true;
                        foreach (var colony in player.Colonies)
                        {
                            if (!colonies.ContainsKey(colony))
                            {
                                colonies.Add(colony, false);
                            }
                        }
                    }
                }
            }

            return list;
        }

        static List<ScienceKey> GetBiomeScienceSpan(List<Colony> colonies)
        {
            List<ScienceKey> sciences = new List<ScienceKey>();

            foreach (var colony in colonies)
            {
                foreach (var science in sciencebiomes)
                {
                    if (HasScienceBiome(colony, science))
                    {
                        if (!sciences.Contains(science))
                        {
                            sciences.Add(science);
                        }
                    }
                }

                if (sciences.Count == sciencebiomes.Count)
                {
                    break;
                }
            }

            return sciences;
        }

        static void ApplyBiomeScienceToRelatives(Colony start)
        {
            if (start == null ||
                start.ScienceData == null)
            {
                return;
            }

            Initialize();

            List<Colony> colonies = GetAllColonies(start);
            List<ScienceKey> sciences = GetBiomeScienceSpan(colonies);
            foreach (var colony in colonies)
            {
                foreach (var science in sciences)
                {
                    AddScienceBiome(colony, science);
                }
            }
        }

        public static bool HasScienceBiome(Colony colony, ScienceKey sciencebiome)
        {
            if (colony == null ||
                colony.ScienceData == null ||
                colony.ScienceData.CompletedScience == null ||
                colony.ScienceData.ScienceMask == null)
            {
                return false;
            }

            return
                colony.ScienceData.CompletedScience.Contains(sciencebiome) &&
                colony.ScienceData.ScienceMask.GetAvailable(sciencebiome);
        }

        static void AddScienceBiome(Colony colony, ScienceKey sciencebiome)
        {
            if (colony == null || colony.ScienceData == null)
            {
                return;
            }

            if (colony.ScienceData.CompletedScience != null)
            {
                colony.ScienceData.CompletedScience.AddIfUnique(sciencebiome);
            }

            if (colony.ScienceData.ScienceMask != null)
            {
                colony.ScienceData.ScienceMask.SetAvailable(sciencebiome, true);
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnModifyResearchables, "grasmanek94.BiomeBackPropagation.OnModifyResearchables", float.MaxValue)]
        static void OnModifyResearchables(Dictionary<string, DefaultResearchable> researchables)
        {
            foreach (var researchable in researchables)
            {
                researchable.Value.RequiredScienceBiome = "";
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnLoadingColony, "grasmanek94.BiomeBackPropagation.OnLoadingColony")]
        static void OnLoadingColony(Colony colony, JSONNode node)
        {
            ApplyBiomeScienceToRelatives(colony);
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnCreatedColony, "grasmanek94.BiomeBackPropagation.OnCreatedColony")]
        static void OnCreatedColony(Colony colony)
        {
            ApplyBiomeScienceToRelatives(colony);
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnActiveColonyChanges, "grasmanek94.BiomeBackPropagation.OnActiveColonyChanges")]
        static void OnActiveColonyChanges(Players.Player player, Colony oldColony, Colony newColony)
        {
            ApplyBiomeScienceToRelatives(oldColony);
            ApplyBiomeScienceToRelatives(newColony);
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerRespawn, "grasmanek94.BiomeBackPropagation.OnPlayerRespawn")]
        static void OnPlayerRespawn(Players.Player player)
        {
            ApplyBiomeScienceToRelatives(player.ActiveColony);
        }
    }
}
