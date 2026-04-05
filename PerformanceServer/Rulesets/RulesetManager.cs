// Copyright (c) 2025 GooGuTeam
// Licensed under the MIT Licence. See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu;
using System.Reflection;

namespace PerformanceServer.Rulesets
{
    public class RulesetManager : IRulesetManager
    {
        private readonly ILogger<RulesetManager> _logger;

        private const string RulesetLibraryPrefix = "osu.Game.Rulesets";

        private readonly Dictionary<string, Ruleset> _rulesets = new();
        private readonly Dictionary<int, Ruleset> _rulesetsById = new();
        private readonly List<string> _hasPerformanceCalculatorRulesets = new();
        private readonly List<string> _hasDifficultyCalculatorRulesets = new();

        public RulesetManager(ILogger<RulesetManager> logger)
        {
            _logger = logger;
            LoadOfficialRulesets();
            LoadFromDisk();
        }

        private static Tuple<bool, bool> GetAttributeTypesForRuleset(Ruleset ruleset)
        {
            bool hasPerformanceType = false;
            bool hasDifficultyType = false;

            foreach (Type type in Assembly.GetAssembly(ruleset.GetType())!.GetTypes())
            {
                if (type.IsSubclassOf(typeof(PerformanceCalculator)))
                    hasPerformanceType = true;
                else if (type.IsSubclassOf(typeof(DifficultyCalculator)))
                    hasDifficultyType = true;
            }

            return Tuple.Create(hasPerformanceType, hasDifficultyType);
        }

        private void AddRuleset(Ruleset ruleset)
        {
            if (!_rulesets.TryAdd(ruleset.ShortName, ruleset))
            {
                _logger.LogWarning("Ruleset with short name {shortName} already exists, skipping.", ruleset.ShortName);
                return;
            }

            Tuple<bool, bool> performanceAndDifficultyTypes = GetAttributeTypesForRuleset(ruleset);
            if (performanceAndDifficultyTypes.Item1)
            {
                _hasPerformanceCalculatorRulesets.Add(ruleset.ShortName);
            }

            if (performanceAndDifficultyTypes.Item2)
            {
                _hasDifficultyCalculatorRulesets.Add(ruleset.ShortName);
            }

            if (ruleset is not ILegacyRuleset legacyRuleset)
            {
                return;
            }

            if (!_rulesetsById.TryAdd(legacyRuleset.LegacyID, ruleset))
            {
                _logger.LogWarning("Ruleset with ID {id} already exists, skipping.", legacyRuleset.LegacyID);
            }
        }

        private void LoadOfficialRulesets()
        {
            foreach (Ruleset ruleset in (List<Ruleset>)
                     [new OsuRuleset()])
            {
                AddRuleset(ruleset);
            }
        }

        private void LoadFromDisk()
        {
            if (!Directory.Exists(AppSettings.RulesetsPath))
            {
                return;
            }

            string[] rulesets = Directory.GetFiles(AppSettings.RulesetsPath, $"{RulesetLibraryPrefix}.*.dll");

            foreach (string ruleset in rulesets.Where(f => !f.Contains(@"Tests")))
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(ruleset);
                    Type? rulesetType = assembly.GetTypes()
                        .FirstOrDefault(t => t.IsSubclassOf(typeof(Ruleset)) && !t.IsAbstract);

                    if (rulesetType == null)
                    {
                        continue;
                    }

                    Ruleset instance = (Ruleset)Activator.CreateInstance(rulesetType)!;
                    _logger.LogInformation("Loading ruleset {ruleset}", ruleset);
                    AddRuleset(instance);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to load ruleset from {ruleset}: {ex}", ruleset, ex);
                }
            }
        }

        public Ruleset GetRuleset(int rulesetId)
        {
            return _rulesetsById.TryGetValue(rulesetId, out Ruleset? ruleset)
                ? ruleset
                : throw new ArgumentException("Invalid ruleset ID provided.");
        }

        public Ruleset GetRuleset(string shortName)
        {
            return _rulesets.TryGetValue(shortName, out Ruleset? ruleset)
                ? ruleset
                : throw new ArgumentException("Invalid ruleset name provided.");
        }

        public Ruleset GetRuleset(INeedsRuleset body, int defaultRulesetId = -1)
        {
            Ruleset ruleset;
            if (!string.IsNullOrEmpty(body.RulesetName))
            {
                ruleset = GetRuleset(body.RulesetName);
            }
            else if (body.RulesetId != null)
            {
                ruleset = GetRuleset(body.RulesetId.Value);
            }
            else if (defaultRulesetId >= -1)
            {
                ruleset = GetRuleset(defaultRulesetId);
            }
            else
            {
                throw new ArgumentException("No ruleset provided.");
            }

            return ruleset;
        }

        public IEnumerable<Ruleset> GetRulesets()
        {
            return _rulesets.Values;
        }

        public IEnumerable<Ruleset> GetHasPerformCalculatorRulesets()
        {
            return _hasPerformanceCalculatorRulesets.Select(shortName => _rulesets[shortName]);
        }

        public IEnumerable<Ruleset> GetHasDifficultyCalculatorRulesets()
        {
            return _hasDifficultyCalculatorRulesets.Select(shortName => _rulesets[shortName]);
        }
    }
}
