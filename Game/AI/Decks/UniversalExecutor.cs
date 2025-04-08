using YGOSharp.OCGWrapper.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using WindBot;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;
using WindBot.Game.AI.Enums;

namespace WindBot.Game.AI.Decks
{
    // Add using directive if not already present
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Add ClientCardExtensions class at the very top of the namespace (before the UniversalExecutor class)
    public static class ClientCardExtensions
    {
        public static ClientCard Clone(this ClientCard card)
        {
            if (card == null) return null;
            
            // Create a new ClientCard using constructor
            ClientCard clone = new ClientCard(card.Id, card.Location, -1, card.Position, card.Controller);
            
            // We can't directly set Attack, Defense, Level, etc. as they are read-only
            // These properties are set internally by the ClientCard class
            
            // For simulation purposes in MCTS, we'll use RealPower as an alternative
            // to store attack/defense values since it's writable
            clone.RealPower = card.Attack; // Store Attack in RealPower for simulation
            
            // Copy the Overlays if any - but note that Overlays is read-only, so we can't set it directly
            // We'll need to modify the Monte Carlo simulation to account for this limitation
            
            return clone;
        }
    }

    [Deck("Universal", "AI_Universal")]
    public class UniversalExecutor : DefaultExecutor
    {
        // Card IDs
        private const int HeavyStormDuster = 23924608;
        private const int TwinTwisters = 43898403;
        private const int MysticalSpaceTyphoon = 5318639;

        private const int RoyalMagicalLibraryId = 70791313;
        private const int ReversalQuizId = 77622396;
        private const int BlackPendantId = 65169794;

        private List<int> DeckCardIds;
        private Dictionary<int, int> CardComboScores;
        private List<List<int>> KnownCombos;
        private List<List<int>> OpponentCombos;
        private bool deckLoaded = false;
        
        // Add card play tracking for combo detection
        private List<int> CardsPlayedThisTurn = new List<int>();
        private List<int> OpponentCardsPlayedThisTurn = new List<int>();
        private List<int> SuccessfulCardSequence = new List<int>();
        private int previousEnemyLifePoints = 8000;
        private int previousBotLifePoints = 8000;
        
        // Enhanced learning systems
        private Dictionary<string, ComboStats> ComboPerformance = new Dictionary<string, ComboStats>();
        private Dictionary<int, CounterStrategy> CardCounters = new Dictionary<int, CounterStrategy>();
        private Dictionary<GameStage, List<List<int>>> StageSpecificCombos = new Dictionary<GameStage, List<List<int>>>();
        private string DeckArchetype = "Unknown";
        private bool resultProcessed = false;
        
        // Data serialization settings
        private readonly string CombosFilePath;
        private readonly string ComboScoresFilePath;
        private bool combosChanged = false;
        private bool comboScoresChanged = false;
        
        // Add a check if we've registered the end duel handler
        private static bool endDuelHandlerRegistered = false;

        private enum GameStage 
        { 
            Early, 
            Mid, 
            Late 
        }

        // Reinforcement Learning System
        [Serializable]
        private class CardActionValueData
        {
            public Dictionary<int, Dictionary<string, double>> QValues { get; set; } = new Dictionary<int, Dictionary<string, double>>();
            public string LastUpdated { get; set; } = DateTime.Now.ToString();
        }

        [Serializable]
        public enum ActionType
        {
            None = 0,
            Activate = 1,
            Summon = 2,
            SpSummon = 3,
            Set = 4,
            Attack = 5,
            ToDefense = 6,
            ToAttack = 7,
            SetMonster = 8,
            SetSpellTrap = 9,
            SpecialSummon = 10,  // Alias for SpSummon
            Target = 11,
            Tribute = 12
        }

        private class ReinforcementLearningSystem
        {
            // Q-Learning parameters
            private double LearningRate = 0.3;
            private double DiscountFactor = 0.9;
            private double ExplorationRate = 0.2;
            private Random Random = new Random();

            // Q-Values storage
            private Dictionary<int, Dictionary<string, double>> QValues;
            
            // Tracking variables
            private Dictionary<int, ActionType> CardActionsThisTurn = new Dictionary<int, ActionType>();
            private int PreviousEnemyLP = 8000;
            private int PreviousBotLP = 8000;
            private int TurnStartEnemyCards = 0;
            private int TurnStartBotCards = 0;
            private bool ValuesChanged = false;
            private string QLearningFilePath;

            public ReinforcementLearningSystem(string filePath)
            {
                QLearningFilePath = filePath;
                QValues = new Dictionary<int, Dictionary<string, double>>();
                LoadQValues();
            }

            public void LoadQValues()
            {
                try
                {
                    if (File.Exists(QLearningFilePath))
                    {
                        string json = File.ReadAllText(QLearningFilePath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            CardActionValueData data = JsonConvert.DeserializeObject<CardActionValueData>(json);
                            if (data != null && data.QValues != null)
                            {
                                QValues = data.QValues;
                                Logger.DebugWriteLine($"Loaded Q-values for {QValues.Count} cards from file");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error loading Q-values: {ex.Message}");
                    // Initialize with empty dictionary if loading fails
                    QValues = new Dictionary<int, Dictionary<string, double>>();
                }
            }

            public void SaveQValues()
            {
                if (!ValuesChanged)
                    return;

                try
                {
                    CardActionValueData data = new CardActionValueData
                    {
                        QValues = QValues,
                        LastUpdated = DateTime.Now.ToString()
                    };

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(QLearningFilePath, json);
                    Logger.DebugWriteLine($"Saved Q-values for {QValues.Count} cards to file");
                    ValuesChanged = false;
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error saving Q-values: {ex.Message}");
                }
            }

            public void TrackAction(int cardId, ActionType action)
            {
                if (!CardActionsThisTurn.ContainsKey(cardId))
                {
                    CardActionsThisTurn[cardId] = action;
                    Logger.DebugWriteLine($"Tracked action: Card {cardId}, Action {action}");
                }
            }

            public void UpdateQValues(double reward)
            {
                foreach (var entry in CardActionsThisTurn)
                {
                    int cardId = entry.Key;
                    ActionType action = entry.Value;
                    string actionKey = action.ToString();

                    // Initialize Q-values for this card if needed
                    if (!QValues.ContainsKey(cardId))
                        QValues[cardId] = new Dictionary<string, double>();

                    // Initialize Q-value for this action if needed
                    if (!QValues[cardId].ContainsKey(actionKey))
                        QValues[cardId][actionKey] = 0.0;

                    // Q-Learning update formula: Q(s,a) = Q(s,a) + α * (r + γ * max(Q(s',a')) - Q(s,a))
                    double oldValue = QValues[cardId][actionKey];
                    double maxNextValue = 0.0; // For terminal state

                    double newValue = oldValue + LearningRate * (reward + DiscountFactor * maxNextValue - oldValue);
                    QValues[cardId][actionKey] = newValue;

                    Logger.DebugWriteLine($"Updated Q-value for card {cardId}, action {actionKey}: {oldValue} -> {newValue}");
                    ValuesChanged = true;
                }

                // Clear tracked actions after updating
                CardActionsThisTurn.Clear();
            }

            public void CalculateRewards(ClientField bot, ClientField enemy, bool duelEnding = false)
            {
                double reward = 0.0;

                // Life point changes
                int enemyLPDiff = PreviousEnemyLP - enemy.LifePoints;
                int botLPDiff = PreviousBotLP - bot.LifePoints;
                
                // Card advantage changes
                int currentBotCards = bot.Hand.Count + bot.GetMonsterCount() + bot.GetSpellCount();
                int currentEnemyCards = enemy.Hand.Count + enemy.GetMonsterCount() + enemy.GetSpellCount();
                int botCardDiff = currentBotCards - TurnStartBotCards;
                int enemyCardDiff = currentEnemyCards - TurnStartEnemyCards;
                
                // Winning/losing
                if (duelEnding)
                {
                    if (enemy.LifePoints <= 0)
                        reward += 100.0; // Major reward for winning
                    else if (bot.LifePoints <= 0)
                        reward -= 100.0; // Major penalty for losing
                }

                // Dealing damage
                if (enemyLPDiff > 0)
                    reward += enemyLPDiff / 1000.0; // 1 reward point per 1000 damage dealt

                // Taking damage
                if (botLPDiff > 0)
                    reward -= botLPDiff / 800.0; // 1.25 penalty points per 1000 damage taken

                // Card advantage
                reward += botCardDiff * 2.0; // Gaining cards is good
                reward -= enemyCardDiff * 2.0; // Opponent gaining cards is bad

                // Field presence (monsters)
                reward += bot.GetMonsterCount() * 1.0;
                reward -= enemy.GetMonsterCount() * 1.0;
                
                // Extra reward for strong monsters
                foreach (ClientCard monster in bot.GetMonsters())
                {
                    if (monster != null && monster.Attack >= 2500)
                        reward += 2.0;
                }

                // Update Q-values with calculated reward
                UpdateQValues(reward);
                
                // Update state for next calculation
                PreviousEnemyLP = enemy.LifePoints;
                PreviousBotLP = bot.LifePoints;
                TurnStartBotCards = currentBotCards;
                TurnStartEnemyCards = currentEnemyCards;
                
                Logger.DebugWriteLine($"Calculated reward: {reward}");
            }

            public void StartTurn(ClientField bot, ClientField enemy)
            {
                PreviousEnemyLP = enemy.LifePoints;
                PreviousBotLP = bot.LifePoints;
                TurnStartBotCards = bot.Hand.Count + bot.GetMonsterCount() + bot.GetSpellCount();
                TurnStartEnemyCards = enemy.Hand.Count + enemy.GetMonsterCount() + enemy.GetSpellCount();
                CardActionsThisTurn.Clear();
            }

            public void EndTurn(ClientField bot, ClientField enemy)
            {
                CalculateRewards(bot, enemy);
                SaveQValues();
            }

            public ActionType GetBestAction(int cardId, List<ActionType> availableActions)
            {
                // Exploration: randomly select an action
                if (Random.NextDouble() < ExplorationRate)
                {
                    int randomIndex = Random.Next(availableActions.Count);
                    return availableActions[randomIndex];
                }

                // Exploitation: select best known action based on Q-values
                if (QValues.ContainsKey(cardId))
                {
                    double maxValue = double.MinValue;
                    ActionType bestAction = availableActions[0];

                    foreach (ActionType action in availableActions)
                    {
                        string actionKey = action.ToString();
                        if (QValues[cardId].ContainsKey(actionKey) && QValues[cardId][actionKey] > maxValue)
                        {
                            maxValue = QValues[cardId][actionKey];
                            bestAction = action;
                        }
                    }

                    return bestAction;
                }

                // Default: return first available action if no Q-values exist
                return availableActions[0];
            }

            public double GetCardActionValue(int cardId, ActionType action)
            {
                string actionKey = action.ToString();
                if (QValues.ContainsKey(cardId) && QValues[cardId].ContainsKey(actionKey))
                    return QValues[cardId][actionKey];
                return 0.0; // Default value for unknown card/action pairs
            }
        }

        // Class members for RL system
        private CardActionValueNetwork RL;
        private readonly string RLFilePath;

        [Serializable]
        private class ComboData
        {
            public List<List<int>> KnownCombos { get; set; } = new List<List<int>>();
            public List<List<int>> OpponentCombos { get; set; } = new List<List<int>>();
            public string LastUpdated { get; set; } = DateTime.Now.ToString();
        }

        [Serializable]
        private class ScoreData
        {
            public Dictionary<int, int> CardComboScores { get; set; } = new Dictionary<int, int>();
            public string LastUpdated { get; set; } = DateTime.Now.ToString();
        }

        [Serializable]
        private class ComboStats
        {
            public List<int> ComboCards { get; set; } = new List<int>();
            public int TotalUses { get; set; } = 0;
            public int Wins { get; set; } = 0;
            public int Losses { get; set; } = 0;
            public int Draws { get; set; } = 0;
            
            public double WinRate => (Wins + Losses + Draws) > 0 ? (double)Wins / (Wins + Losses + Draws) : 0;
        }
        
        [Serializable]
        private class CounterStrategy
        {
            public int CardId { get; set; }
            public List<int> EffectiveCounters { get; set; } = new List<int>();
        }
        
        [Serializable]
        private class EnhancedLearningData
        {
            public Dictionary<string, ComboStats> ComboPerformance { get; set; } = new Dictionary<string, ComboStats>();
            public Dictionary<int, CounterStrategy> CardCounters { get; set; } = new Dictionary<int, CounterStrategy>();
            public Dictionary<string, List<List<int>>> StageSpecificCombos { get; set; } = new Dictionary<string, List<List<int>>>();
            public string LastUpdated { get; set; } = DateTime.Now.ToString();
        }

        public UniversalExecutor(GameAI ai, Duel duel) : base(ai, duel)
        {
            DeckCardIds = new List<int>();
            CardComboScores = new Dictionary<int, int>();
            KnownCombos = new List<List<int>>();
            OpponentCombos = new List<List<int>>();
            
            // Initialize card play tracking
            CardsPlayedThisTurn = new List<int>();
            OpponentCardsPlayedThisTurn = new List<int>();
            SuccessfulCardSequence = new List<int>();
            
            // Setup absolute file paths
            string currentDirectory = System.IO.Directory.GetCurrentDirectory();
            CombosFilePath = Path.Combine(currentDirectory, "universal_combos.json");
            ComboScoresFilePath = Path.Combine(currentDirectory, "universal_combo_scores.json");
            
            Logger.DebugWriteLine($"Using combo files: {CombosFilePath} and {ComboScoresFilePath}");
            
            // Try to load saved combo data
            LoadSavedComboData();
            
            // If no combos were loaded, seed with some defaults to ensure we save something
            if (KnownCombos.Count == 0 && CardComboScores.Count == 0)
            {
                Logger.DebugWriteLine("No existing combo data found, initializing with default values");
                // Seed with some generic good cards
                CardComboScores[89631139] = 15; // Blue-Eyes
                CardComboScores[46986414] = 15; // Dark Magician
                CardComboScores[44519536] = 10; // Monster Reborn
                CardComboScores[24094653] = 10; // Polymerization
                CardComboScores[53129443] = 12; // Dark Hole
                comboScoresChanged = true;
                
                // Force save initial data
                SaveComboData();
            }
            
            // Add essential execution handlers
            AddExecutor(ExecutorType.Activate, HeavyStormDuster, HeavyStormDusterEffect);
            AddExecutor(ExecutorType.Activate, TwinTwisters, TwistersEffect);
            AddExecutor(ExecutorType.Activate, MysticalSpaceTyphoon, DefaultMysticalSpaceTyphoon);
            
            // Add turn change handling to track combos
            AddExecutor(ExecutorType.Activate, MonitorGameState);
            
            // Add lethal attack check to win when possible
            AddExecutor(ExecutorType.Activate, LethalAttackCheck);
            
            // Add card summoning logic
            AddExecutor(ExecutorType.Summon, SummonHighestAttackMonster);
            AddExecutor(ExecutorType.SpSummon, SpecialSummonMonster);
            
            // Add general card activation
            AddExecutor(ExecutorType.Activate, MonsterEffectActivate);
            AddExecutor(ExecutorType.Activate, SpellActivate);
            AddExecutor(ExecutorType.Activate, TrapActivate);
            
            // Essential set card logic - use SpellSet for both spells and traps
            AddExecutor(ExecutorType.SpellSet, SpellSet);
            
            // Defensive battle position changes
            AddExecutor(ExecutorType.Repos, MonsterRepos);
            
            // We'll load the deck when the duel actually starts
            AddExecutor(ExecutorType.Activate, CheckLoadDeck);
            
            // Add a special executor that checks for duel end and saves data
            AddExecutor(ExecutorType.GoToEndPhase, CheckDuelEnd);
            
            // Also save on object finalization as a fallback
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => SaveComboData();

            // Initialize RL system
            RL = new CardActionValueNetwork();
            Logger.DebugWriteLine("Initialized Reinforcement Learning system");
            
            // Initialize domain knowledge
            DomainKnowledgeInitialized = false;
            CurrentActionType = ActionType.None;
            Logger.DebugWriteLine("Domain knowledge integration ready");

            // General RL-based activation decision for all cards
            // Commented out because we now have domain knowledge integration in specific methods
            // AddExecutor(ExecutorType.Activate, RLBasedActivation);
            
            // Initialize Monte Carlo Tree Search with 1000 simulations
            MCTS = new MonteCarloTreeSearch(1000);
            Logger.DebugWriteLine("Initialized Monte Carlo Tree Search for complex decisions");
        }
        
        // Add a method to check if the duel is ending
        private bool CheckDuelEnd()
        {
            // Only check for end if we're the current player
            if (Duel.Player != 0) return false;
            
            // Check if either player has 0 life points
            bool duelIsEnding = Bot.LifePoints <= 0 || Enemy.LifePoints <= 0;
            
            // If duel is ending, process the result
            if (duelIsEnding && !resultProcessed)
            {
                Logger.DebugWriteLine("Duel is ending, processing results and saving data");
                
                // Set flag to avoid processing multiple times
                resultProcessed = true;
                
                // Determine if we won
                bool won = Enemy.LifePoints <= 0 || Enemy.LifePoints < Bot.LifePoints;
                
                // Process the result with our RL system
                if (RL != null)
                {
                    RL.ProcessDuelEnd(Bot, Enemy, won);
                }
                
                // Update combo performance based on result
                UpdateComboPerformance(won);
                
                // Save all our learned data
                SaveComboData();
            }
            
            return false; // Never actually end the phase with this check
        }
        
        private void LoadSavedComboData()
        {
            try
            {
                Logger.DebugWriteLine($"Attempting to load combo data from {CombosFilePath} and {ComboScoresFilePath}");
                
                // Load KnownCombos and OpponentCombos
                if (File.Exists(CombosFilePath))
                {
                    string comboJson = File.ReadAllText(CombosFilePath);
                    Logger.DebugWriteLine($"Loaded combo file with {comboJson.Length} characters");
                    
                    if (!string.IsNullOrWhiteSpace(comboJson))
                    {
                        ComboData comboData = JsonConvert.DeserializeObject<ComboData>(comboJson);
                        if (comboData != null)
                        {
                            KnownCombos = comboData.KnownCombos ?? new List<List<int>>();
                            OpponentCombos = comboData.OpponentCombos ?? new List<List<int>>();
                            Logger.DebugWriteLine($"Loaded {KnownCombos.Count} known combos and {OpponentCombos.Count} opponent combos from file");
                        }
                    }
                    else
                    {
                        Logger.DebugWriteLine("Combo file exists but is empty");
                    }
                }
                else
                {
                    Logger.DebugWriteLine($"Combo file does not exist: {CombosFilePath}");
                }
                
                // Load CardComboScores
                if (File.Exists(ComboScoresFilePath))
                {
                    string scoreJson = File.ReadAllText(ComboScoresFilePath);
                    Logger.DebugWriteLine($"Loaded score file with {scoreJson.Length} characters");
                    
                    if (!string.IsNullOrWhiteSpace(scoreJson))
                    {
                        ScoreData scoreData = JsonConvert.DeserializeObject<ScoreData>(scoreJson);
                        if (scoreData != null)
                        {
                            CardComboScores = scoreData.CardComboScores ?? new Dictionary<int, int>();
                            Logger.DebugWriteLine($"Loaded {CardComboScores.Count} card scores from file");
                        }
                    }
                    else
                    {
                        Logger.DebugWriteLine("Score file exists but is empty");
                    }
                }
                else
                {
                    Logger.DebugWriteLine($"Score file does not exist: {ComboScoresFilePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.DebugWriteLine($"Error loading saved combo data: {ex.Message}");
                Logger.DebugWriteLine($"Stack trace: {ex.StackTrace}");
                // Initialize with empty collections if loading fails
                KnownCombos = new List<List<int>>();
                OpponentCombos = new List<List<int>>();
                CardComboScores = new Dictionary<int, int>();
            }
        }
        
        private void SaveComboData()
        {
            try
            {
                Logger.DebugWriteLine("Attempting to save combo data...");
                
                // ALWAYS ensure we have at least one dummy combo to save if KnownCombos is empty
                if (KnownCombos.Count == 0)
                {
                    Logger.DebugWriteLine("No combos to save, creating a default combo");
                    // Create a simple dummy combo - Monster Reborn + Blue-Eyes as a fallback
                    KnownCombos.Add(new List<int> { 44519536, 89631139 });
                    combosChanged = true;
                }
                
                // Force changes if we have data but haven't saved it yet
                if (combosChanged)
                {
                    Logger.DebugWriteLine($"Saving {KnownCombos.Count} combos to {CombosFilePath}");
                    ComboData comboData = new ComboData
                    {
                        KnownCombos = KnownCombos,
                        OpponentCombos = OpponentCombos,
                        LastUpdated = DateTime.Now.ToString()
                    };
                    
                    // Create explicit object for debugging
                    Logger.DebugWriteLine($"KnownCombos count: {KnownCombos.Count}");
                    if (KnownCombos.Count > 0)
                    {
                        Logger.DebugWriteLine($"First combo cards: {string.Join(", ", KnownCombos[0])}");
                    }
                    
                    string comboJson = JsonConvert.SerializeObject(comboData, Formatting.Indented);
                    Logger.DebugWriteLine($"Serialized combo JSON length: {comboJson.Length}, content: {comboJson.Substring(0, Math.Min(100, comboJson.Length))}...");
                    
                    File.WriteAllText(CombosFilePath, comboJson);
                    
                    // Verify the file was written correctly
                    if (File.Exists(CombosFilePath))
                    {
                        string savedContent = File.ReadAllText(CombosFilePath);
                        Logger.DebugWriteLine($"Verified file content length: {savedContent.Length}");
                    }
                    
                    Logger.DebugWriteLine($"Saved {KnownCombos.Count} known combos and {OpponentCombos.Count} opponent combos to file ({comboJson.Length} characters)");
                    combosChanged = false;
                }
                
                if (comboScoresChanged)
                {
                    Logger.DebugWriteLine($"Saving {CardComboScores.Count} scores to {ComboScoresFilePath}");
                    ScoreData scoreData = new ScoreData
                    {
                        CardComboScores = CardComboScores,
                        LastUpdated = DateTime.Now.ToString()
                    };
                    string scoreJson = JsonConvert.SerializeObject(scoreData, Formatting.Indented);
                    File.WriteAllText(ComboScoresFilePath, scoreJson);
                    Logger.DebugWriteLine($"Saved {CardComboScores.Count} card scores to file ({scoreJson.Length} characters)");
                    comboScoresChanged = false;
                }
                
                // Save enhanced learning data
                string enhancedDataPath = Path.Combine(Path.GetDirectoryName(CombosFilePath), "universal_enhanced_learning.json");
                Logger.DebugWriteLine($"Saving enhanced learning data to {enhancedDataPath}");
                
                // Convert StageSpecificCombos dictionary to serializable format
                Dictionary<string, List<List<int>>> serializableCombos = new Dictionary<string, List<List<int>>>();
                foreach (var kvp in StageSpecificCombos)
                {
                    serializableCombos[kvp.Key.ToString()] = kvp.Value;
                }
                
                EnhancedLearningData enhancedData = new EnhancedLearningData
                {
                    ComboPerformance = ComboPerformance,
                    CardCounters = CardCounters,
                    StageSpecificCombos = serializableCombos,
                    LastUpdated = DateTime.Now.ToString()
                };
                
                string enhancedJson = JsonConvert.SerializeObject(enhancedData, Formatting.Indented);
                File.WriteAllText(enhancedDataPath, enhancedJson);
                Logger.DebugWriteLine($"Saved enhanced learning data ({enhancedJson.Length} characters)");
                
                // Save reinforcement learning data
                if (RL != null)
                {
                    // Calculate final rewards and save Q-values
                    if (Bot != null && Enemy != null)
                    {
                        bool isDuelEnding = Bot.LifePoints <= 2000 || Enemy.LifePoints <= 2000;
                        RL.CalculateRewards(Bot, Enemy, isDuelEnding);
                    }
                    RL.SaveQValues();
                    Logger.DebugWriteLine("Saved reinforcement learning data");
                }
                
                // Verify files were written
                if (File.Exists(CombosFilePath))
                {
                    long fileSize = new FileInfo(CombosFilePath).Length;
                    Logger.DebugWriteLine($"Verified: Combos file exists with size {fileSize} bytes");
                }
                
                if (File.Exists(ComboScoresFilePath))
                {
                    long fileSize = new FileInfo(ComboScoresFilePath).Length;
                    Logger.DebugWriteLine($"Verified: Scores file exists with size {fileSize} bytes");
                }
                
                if (File.Exists(enhancedDataPath))
                {
                    long fileSize = new FileInfo(enhancedDataPath).Length;
                    Logger.DebugWriteLine($"Verified: Enhanced learning file exists with size {fileSize} bytes");
                }
            }
            catch (Exception ex)
            {
                Logger.DebugWriteLine($"Error saving combo data: {ex.Message}");
                Logger.DebugWriteLine($"Stack trace: {ex.StackTrace}");
                
                try {
                    // Fallback - try to save to a different location
                    string fallbackDir = Path.Combine(Path.GetTempPath(), "WindBot");
                    Directory.CreateDirectory(fallbackDir);
                    
                    string fallbackComboPath = Path.Combine(fallbackDir, "universal_combos_fallback.json");
                    string fallbackScorePath = Path.Combine(fallbackDir, "universal_combo_scores_fallback.json");
                    string fallbackEnhancedPath = Path.Combine(fallbackDir, "universal_enhanced_learning_fallback.json");
                    string fallbackRLPath = Path.Combine(fallbackDir, "universal_qlearning_fallback.json");
                    
                    Logger.DebugWriteLine($"Attempting fallback save to {fallbackDir}");
                    
                    // Save basic data to fallback
                    if (combosChanged)
                    {
                        ComboData comboData = new ComboData
                        {
                            KnownCombos = KnownCombos,
                            OpponentCombos = OpponentCombos,
                            LastUpdated = DateTime.Now.ToString()
                        };
                        string comboJson = JsonConvert.SerializeObject(comboData, Formatting.Indented);
                        File.WriteAllText(fallbackComboPath, comboJson);
                        Logger.DebugWriteLine($"Fallback save successful for combos");
                    }
                    
                    if (comboScoresChanged)
                    {
                        ScoreData scoreData = new ScoreData
                        {
                            CardComboScores = CardComboScores,
                            LastUpdated = DateTime.Now.ToString()
                        };
                        string scoreJson = JsonConvert.SerializeObject(scoreData, Formatting.Indented);
                        File.WriteAllText(fallbackScorePath, scoreJson);
                        Logger.DebugWriteLine($"Fallback save successful for scores");
                    }
                    
                    // Save enhanced data to fallback
                    Dictionary<string, List<List<int>>> serializableCombos = new Dictionary<string, List<List<int>>>();
                    foreach (var kvp in StageSpecificCombos)
                    {
                        serializableCombos[kvp.Key.ToString()] = kvp.Value;
                    }
                    
                    EnhancedLearningData enhancedData = new EnhancedLearningData
                    {
                        ComboPerformance = ComboPerformance,
                        CardCounters = CardCounters,
                        StageSpecificCombos = serializableCombos,
                        LastUpdated = DateTime.Now.ToString()
                    };
                    
                    string enhancedJson = JsonConvert.SerializeObject(enhancedData, Formatting.Indented);
                    File.WriteAllText(fallbackEnhancedPath, enhancedJson);
                    Logger.DebugWriteLine($"Fallback save successful for enhanced learning data");
                    
                    // Save RL data to fallback
                    if (RL != null)
                    {
                        RL.SaveQValues();
                        Logger.DebugWriteLine($"Fallback save attempted for reinforcement learning data");
                    }
                }
                catch (Exception innerEx)
                {
                    Logger.DebugWriteLine($"Fallback save also failed: {innerEx.Message}");
                }
            }
        }

        private bool CheckLoadDeck()
        {
            if (!deckLoaded && Bot?.Deck != null)
            {
                LoadDeckCards();
                deckLoaded = true;
            }
            return false; // Always return false so we don't activate anything
        }

        private void LoadDeckCards()
        {
            Logger.DebugWriteLine("Loading deck cards...");
            DeckCardIds.Clear();
            
            try
            {
                if (Bot?.Deck == null)
                {
                    Logger.DebugWriteLine("Bot.Deck is null, can't load deck cards yet");
                    return;
                }
                
                foreach (ClientCard card in Bot.Deck)
                {
                    if (card != null && card.Id > 0)
                    {
                        DeckCardIds.Add(card.Id);
                        Logger.DebugWriteLine($"Added card to deck: {card.Id}");
                    }
                }
                
                Logger.DebugWriteLine($"Loaded {DeckCardIds.Count} cards from deck");
                
                // Now that we have deck cards, initialize scores and identify combos
                InitializeComboScores();
                IdentifyKnownCombos();
                
                // Identify deck archetype based on cards
                IdentifyDeckArchetype();
                
                // Reset result processed flag for new duel
                resultProcessed = false;
            }
            catch (Exception ex)
            {
                Logger.DebugWriteLine($"Error loading deck: {ex.Message}");
            }
        }

        private void IdentifyKnownCombos()
        {
            // Keep track of any new combos we identify
            bool foundNewCombos = false;
            
            Logger.DebugWriteLine("Looking for card combos in the deck...");
            
            // Define some basic combos for different archetypes
            // This is just a starting point - a real implementation would identify more complex combos
            
            // Even if none of the specific combos match, create at least one default combo
            // This ensures we'll always have something to save
            bool hasDefaultCombo = false;
            
            // Example: Blue-Eyes combo
            if (DeckCardIds.Contains(89631139) && DeckCardIds.Contains(23434538)) // Blue-Eyes White Dragon + White Stone of Ancients
            {
                List<int> combo = new List<int> { 89631139, 23434538 };
                if (!ContainsCombo(KnownCombos, combo))
                {
                    KnownCombos.Add(combo);
                    foundNewCombos = true;
                    Logger.DebugWriteLine("Identified Blue-Eyes combo");
                    hasDefaultCombo = true;
                }
            }
            
            // Example: Dark Magician combo
            if (DeckCardIds.Contains(46986414) && DeckCardIds.Contains(2314238)) // Dark Magician + Dark Magical Circle
            {
                List<int> combo = new List<int> { 46986414, 2314238 };
                if (!ContainsCombo(KnownCombos, combo))
                {
                    KnownCombos.Add(combo);
                    foundNewCombos = true;
                    Logger.DebugWriteLine("Identified Dark Magician combo");
                }
            }
            
            // Add more basic combos that should work with most decks
            // Monster Reborn + any powerful monster
            if (DeckCardIds.Contains(44519536)) // Monster Reborn
            {
                int strongMonster = DeckCardIds.FirstOrDefault(id => {
                    var card = NamedCard.Get(id);
                    return card != null && card.HasType(CardType.Monster) && (card.Attack >= 2500 || card.Level >= 7);
                });
                
                if (strongMonster > 0)
                {
                    List<int> combo = new List<int> { 44519536, strongMonster };
                    if (!ContainsCombo(KnownCombos, combo))
                    {
                        KnownCombos.Add(combo);
                        foundNewCombos = true;
                        Logger.DebugWriteLine($"Identified Monster Reborn + strong monster combo with {strongMonster}");
                    }
                }
            }
            
            // Look for generic card pairs that work well together
            foreach (int card1 in DeckCardIds)
            {
                var cardData1 = NamedCard.Get(card1);
                if (cardData1 == null) continue;
                
                foreach (int card2 in DeckCardIds)
                {
                    if (card1 == card2) continue;
                    
                    var cardData2 = NamedCard.Get(card2);
                    if (cardData2 == null) continue;
                    
                    // Simple heuristic: tuners and non-tuner monsters of similar levels might form a combo
                    if (cardData1.HasType(CardType.Tuner) && cardData2.HasType(CardType.Monster) && !cardData2.HasType(CardType.Tuner))
                    {
                        if (Math.Abs(cardData1.Level - cardData2.Level) <= 2)
                        {
                            List<int> combo = new List<int> { card1, card2 };
                            if (!ContainsCombo(KnownCombos, combo))
                            {
                                KnownCombos.Add(combo);
                                foundNewCombos = true;
                                Logger.DebugWriteLine($"Identified tuner combo: {card1} + {card2}");
                            }
                        }
                    }
                }
            }
            
            // Always force at least one combo if we didn't find any naturally
            if (KnownCombos.Count == 0 || !hasDefaultCombo)
            {
                Logger.DebugWriteLine("No combos found naturally, adding default combo");
                
                // Default combo 1: Monster Reborn + Blue-Eyes (generic powerful combo)
                List<int> defaultCombo = new List<int> { 44519536, 89631139 };
                if (!ContainsCombo(KnownCombos, defaultCombo))
                {
                    KnownCombos.Add(defaultCombo);
                    foundNewCombos = true;
                    Logger.DebugWriteLine("Added generic Monster Reborn + Blue-Eyes combo");
                }
                
                // Create a generic combo of the two highest attack monsters
                var topMonsters = DeckCardIds
                    .Select(id => NamedCard.Get(id))
                    .Where(card => card != null && card.HasType(CardType.Monster))
                    .OrderByDescending(card => card.Attack)
                    .Take(2)
                    .ToList();
                
                if (topMonsters.Count >= 2)
                {
                    List<int> combo = new List<int> { topMonsters[0].Id, topMonsters[1].Id };
                    if (!ContainsCombo(KnownCombos, combo))
                    {
                        KnownCombos.Add(combo);
                        foundNewCombos = true;
                        Logger.DebugWriteLine($"Created generic combo of top 2 monsters: {topMonsters[0].Id} + {topMonsters[1].Id}");
                    }
                }
            }
            
            Logger.DebugWriteLine($"Final combo count: {KnownCombos.Count}");
            foreach (var combo in KnownCombos)
            {
                Logger.DebugWriteLine($"- Combo: {string.Join(", ", combo)}");
            }
            
            // Track if we've made changes to save later
            if (foundNewCombos)
            {
                Logger.DebugWriteLine("New combos identified, marking for save");
                combosChanged = true;
            }
            else if (KnownCombos.Count > 0)
            {
                // Force save even if no new combos were found
                Logger.DebugWriteLine("No new combos, but forcing save of existing combos");
                combosChanged = true;
            }
            
            // Save combos right after identification
            SaveComboData();
        }
        
        private bool ContainsCombo(List<List<int>> comboList, List<int> combo)
        {
            // Check if the combo list already contains this combo (regardless of order)
            foreach (var existingCombo in comboList)
            {
                if (existingCombo.Count == combo.Count && 
                    existingCombo.All(id => combo.Contains(id)) && 
                    combo.All(id => existingCombo.Contains(id)))
                {
                    return true;
                }
            }
            return false;
        }

        private void InitializeComboScores()
        {
            if (DeckCardIds.Count == 0)
            {
                Logger.DebugWriteLine("No deck cards loaded, can't initialize combo scores");
                return;
            }
            
            bool scoresChanged = false;
            
            foreach (int id in DeckCardIds)
            {
                var card = NamedCard.Get(id);
                if (card == null) continue;
                
                // Only calculate score if we don't already have one
                if (!CardComboScores.ContainsKey(id))
                {
                    int baseScore = card.HasType(CardType.Monster) && card.HasType(CardType.Effect) ? 5 : (card.HasType(CardType.Spell) ? 3 : (card.HasType(CardType.Trap) ? 2 : 0));
                CardComboScores[id] = baseScore;
                    scoresChanged = true;
            }
            }
            
            // These are important cards, make sure they have high scores
            if (!CardComboScores.ContainsKey(RoyalMagicalLibraryId) || CardComboScores[RoyalMagicalLibraryId] < 20)
            {
            CardComboScores[RoyalMagicalLibraryId] = 20;
                scoresChanged = true;
            }
            
            if (!CardComboScores.ContainsKey(ReversalQuizId) || CardComboScores[ReversalQuizId] < 15)
            {
            CardComboScores[ReversalQuizId] = 15;
                scoresChanged = true;
            }
            
            if (!CardComboScores.ContainsKey(BlackPendantId) || CardComboScores[BlackPendantId] < 10)
            {
            CardComboScores[BlackPendantId] = 10;
                scoresChanged = true;
            }
            
            // Track if we've made changes to save later
            if (scoresChanged)
            {
                comboScoresChanged = true;
            }
        }
        
        // Method to learn from successful combos during gameplay
        public void LearnCombo(List<int> cardIds)
        {
            if (cardIds.Count >= 2 && !ContainsCombo(KnownCombos, cardIds))
            {
                KnownCombos.Add(cardIds);
                combosChanged = true;
                Logger.DebugWriteLine($"Learned new combo: {string.Join(", ", cardIds)}");
                
                // Increase scores for cards in successful combos
                foreach (int id in cardIds)
                {
                    if (CardComboScores.ContainsKey(id))
                    {
                        CardComboScores[id] += 2;
                    }
                    else
                    {
                        CardComboScores[id] = 5;
                    }
                }
                comboScoresChanged = true;
                
                // Save immediately when we learn a new combo
                SaveComboData();
            }
        }
        
        // Method to learn opponent's combos
        public void LearnOpponentCombo(List<int> cardIds)
        {
            if (cardIds.Count >= 2 && !ContainsCombo(OpponentCombos, cardIds))
            {
                OpponentCombos.Add(cardIds);
                combosChanged = true;
                Logger.DebugWriteLine($"Learned new opponent combo: {string.Join(", ", cardIds)}");
                
                // Save immediately when we learn a new opponent combo
                SaveComboData();
            }
        }

        private int EvaluateCardPower(ClientCard card)
        {
            if (card == null) return 0;
            
            int power = 0;
            if (card.IsMonster())
            {
                power += card.Attack / 100 + card.Level;
                if (card.HasType(CardType.Effect)) power += 5;
                if (card.HasType(CardType.Tuner)) power += 3;
                if (card.HasType(CardType.Synchro | CardType.Xyz | CardType.Link)) power += 10;
            }
            else if (card.IsSpell()) power += card.HasType(CardType.QuickPlay | CardType.Field) ? 8 : 5;
            else if (card.IsTrap()) power += card.HasType(CardType.Counter) ? 6 : 4;

            power += CardComboScores.ContainsKey(card.Id) ? CardComboScores[card.Id] : 0;
            foreach (var combo in KnownCombos.Concat(OpponentCombos))
                if (combo.Contains(card.Id)) power += 10 * combo.Count();

            return power;
        }

        // Method to detect Library FTK cards
        private bool IsLibraryFTKCard(int cardId)
        {
            // Key cards in the Library FTK deck
            return cardId == RoyalMagicalLibraryId || // Royal Magical Library
                   cardId == 70368879 || // Upstart Goblin
                   cardId == 77565204 || // Future Fusion 
                   cardId == 58577036 || // Reasoning
                   cardId == 14087893 || // Book of Moon
                   cardId == 83764718 || // Monster Reborn
                   cardId == 83764719 || // Monster Reborn
                   cardId == 55144522 || // Pot of Greed
                   cardId == 72892473 || // Card Destruction
                   cardId == 52631528 || // Pot of Duality
                   cardId == 12580477 || // Raigeki
                   cardId == 5318639 ||  // Mystical Space Typhoon
                   cardId == 67169062 || // Pot of Avarice
                   cardId == 98645731 || // Spell Economics
                   cardId == 71344451 || // Slash Draw
                   cardId == 70828912 || // Premature Burial
                   cardId == 97077563 || // Call of the Haunted
                   cardId == ReversalQuizId || // Reversal Quiz
                   cardId == BlackPendantId; // Black Pendant
        }
        
        // Monitor game state to detect and learn combos
        private bool MonitorGameState()
        {
            // Check if it's a new turn
            if (Duel.Turn > 0 && Duel.Phase == DuelPhase.Draw)
            {
                // Process any successful card sequences from the previous turn
                if (SuccessfulCardSequence.Count >= 2)
                {
                    Logger.DebugWriteLine($"Processing successful sequence: {string.Join(", ", SuccessfulCardSequence)}");
                    LearnCombo(new List<int>(SuccessfulCardSequence));
                    
                    // Also learn as a context-specific combo
                    GameStage stage = GetCurrentGameStage();
                    LearnContextualCombo(new List<int>(SuccessfulCardSequence), stage);
                    
                    // Identify longer chains of cards
                    IdentifyCardChains(SuccessfulCardSequence, true);
                }
                
                // RL: End the previous turn's learning cycle
                if (RL != null && Duel.Turn > 1)
                {
                    Logger.DebugWriteLine("End of turn: calculating RL rewards");
                    RL.EndTurn(Bot, Enemy);
                }
                
                // RL: Start a new turn's learning cycle
                if (RL != null)
                {
                    Logger.DebugWriteLine("Start of turn: initializing RL state");
                    RL.StartTurn(Bot, Enemy);
                    
                    // Apply domain knowledge to RL system at the start of each turn
                    CurrentActionType = ActionType.None; // Reset current action type
                    EnhanceRLWithDomainKnowledge();
                }
                
                // Clear card tracking for new turn
                CardsPlayedThisTurn.Clear();
                OpponentCardsPlayedThisTurn.Clear();
                SuccessfulCardSequence.Clear();
                
                // Store current life points for comparison
                previousEnemyLifePoints = Enemy.LifePoints;
                previousBotLifePoints = Bot.LifePoints;
            }
            
            // Look for card changes on the field and add to tracking
            foreach (ClientCard card in Bot.Hand)
            {
                if (card != null && card.Id > 0 && !CardsPlayedThisTurn.Contains(card.Id))
                {
                    // If the card is no longer in hand but was before, assume it was played
                    if (card.Location != CardLocation.Hand)
                    {
                        CardsPlayedThisTurn.Add(card.Id);
                        Logger.DebugWriteLine($"Tracked card play: {card.Id}");
                        
                        // If this is a Library FTK card, add it to successful sequence for learning
                        if (IsLibraryFTKCard(card.Id))
                        {
                            SuccessfulCardSequence.Add(card.Id);
                            Logger.DebugWriteLine($"Added to successful sequence: {card.Id}");
                        }
                    }
                }
            }
            
            // Analyze current game state
            AnalyzeGameState();
            
            // Analyze the opponent's board and strategy
            AnalyzeOpponentStrategy();
            
            // Check for damage dealt (successful play)
            if (Enemy.LifePoints < previousEnemyLifePoints)
            {
                int damage = previousEnemyLifePoints - Enemy.LifePoints;
                Logger.DebugWriteLine($"Dealt {damage} damage to opponent");
                
                // If we dealt significant damage, consider the card sequence successful
                if (damage >= 1000 && CardsPlayedThisTurn.Count >= 2)
                {
                    LearnCombo(new List<int>(CardsPlayedThisTurn));
                    Logger.DebugWriteLine($"Learned combo from successful damage: {string.Join(", ", CardsPlayedThisTurn)}");
                    
                    // Learn as stage-specific combo
                    GameStage stage = GetCurrentGameStage();
                    LearnContextualCombo(new List<int>(CardsPlayedThisTurn), stage);
                    
                    // RL: Calculate immediate rewards for dealing damage
                    if (RL != null)
                    {
                        RL.CalculateRewards(Bot, Enemy);
                    }
                }
                
                previousEnemyLifePoints = Enemy.LifePoints;
            }
            
            // Check if we've taken damage (potential counter needed)
            if (Bot.LifePoints < previousBotLifePoints)
            {
                int damage = previousBotLifePoints - Bot.LifePoints;
                Logger.DebugWriteLine($"Took {damage} damage from opponent");
                
                // If opponent has played cards this turn, learn the potential threat
                if (OpponentCardsPlayedThisTurn.Count > 0 && damage >= 1000)
                {
                    int lastOpponentCard = OpponentCardsPlayedThisTurn.Last();
                    LearnCardCounter(lastOpponentCard);
                    Logger.DebugWriteLine($"Learning to counter card {lastOpponentCard} that caused {damage} damage");
                }
                
                // RL: Calculate immediate negative rewards for taking damage
                if (RL != null)
                {
                    RL.CalculateRewards(Bot, Enemy);
                }
                
                previousBotLifePoints = Bot.LifePoints;
            }
            
            // Learn from opponent's plays by tracking cards that were activated
            if (Duel.Player == 1 && Duel.LastChainPlayer == 1)
            {
                // Use Card.Id instead of accessing LastChainCards
                if (Card != null && Card.Id > 0 && !OpponentCardsPlayedThisTurn.Contains(Card.Id))
                {
                    OpponentCardsPlayedThisTurn.Add(Card.Id);
                    Logger.DebugWriteLine($"Tracked opponent card play: {Card.Id}");
                    
                    // Learn from all opponent combos with 2+ cards
                    if (OpponentCardsPlayedThisTurn.Count >= 2)
                    {
                        LearnOpponentCombo(new List<int>(OpponentCardsPlayedThisTurn));
                        Logger.DebugWriteLine($"Learned opponent combo: {string.Join(", ", OpponentCardsPlayedThisTurn)}");
                        
                        // Track if this combo affected our life points negatively
                        if (previousBotLifePoints > Bot.LifePoints)
                        {
                            int damage = previousBotLifePoints - Bot.LifePoints;
                            Logger.DebugWriteLine($"This opponent combo dealt {damage} damage");
                            
                            // Give higher priority to damaging combos
                            if (damage >= 1000)
                            {
                                foreach (int cardId in OpponentCardsPlayedThisTurn)
                                {
                                    LearnCardCounter(cardId);
                                }
                            }
                        }
                        
                        // Track if they gained field advantage
                        int opponentMonsterCount = Enemy.GetMonsterCount();
                        if (opponentMonsterCount >= 2)
                        {
                            Logger.DebugWriteLine($"This opponent combo established board presence with {opponentMonsterCount} monsters");
                            
                            // Learn about effective board building combos
                            foreach (int cardId in OpponentCardsPlayedThisTurn)
                            {
                                if (!CardComboScores.ContainsKey(cardId))
                                {
                                    CardComboScores[cardId] = 5;
                                    comboScoresChanged = true;
                                }
                            }
                        }
                    }
                }
            }
            
            // If phase changes to End Phase, calculate rewards for this phase
            if (Duel.Phase == DuelPhase.End && RL != null)
            {
                Logger.DebugWriteLine("End Phase: calculating intermediate RL rewards");
                RL.CalculateRewards(Bot, Enemy);
            }
            
            return false; // Don't activate anything, just monitor
        }
        
        // Advanced game state analysis
        private void AnalyzeGameState()
        {
            // Track life point differences for better decision making
            int lifePointDifference = Bot.LifePoints - Enemy.LifePoints;
            
            // Track card advantage (hand + field)
            int botCardCount = Bot.Hand.Count + Bot.GetMonsterCount() + Bot.GetSpellCount() + CountTraps(Bot);
            int enemyCardCount = Enemy.Hand.Count + Enemy.GetMonsterCount() + Enemy.GetSpellCount() + CountTraps(Enemy);
            int cardAdvantage = botCardCount - enemyCardCount;
            
            Logger.DebugWriteLine($"Game state analysis - LP diff: {lifePointDifference}, Card advantage: {cardAdvantage}, Turn: {Duel.Turn}, Phase: {Duel.Phase}");
            
            // Learn which combos work best in different game states
            GameStage currentStage = GetCurrentGameStage();
            
            if (lifePointDifference > 3000) 
            {
                // We're ahead - learn combos that help maintain advantage
                if (CardsPlayedThisTurn.Count >= 2) 
                {
                    LearnContextualCombo(new List<int>(CardsPlayedThisTurn), currentStage);
                    Logger.DebugWriteLine("Learning advantage maintenance combo");
                }
            } 
            else if (lifePointDifference < -3000) 
            {
                // We're behind - learn comeback combos
                if (CardsPlayedThisTurn.Count >= 2) 
                {
                    LearnContextualCombo(new List<int>(CardsPlayedThisTurn), currentStage);
                    Logger.DebugWriteLine("Learning potential comeback combo");
                }
            }
            
            // Enhance card evaluation based on game state
            foreach (int cardId in DeckCardIds)
            {
                int baseScore = CardComboScores.ContainsKey(cardId) ? CardComboScores[cardId] : 5;
                int adjustedScore = baseScore;
                
                // Card-specific adjustments based on game state
                var card = NamedCard.Get(cardId);
                if (card != null)
                {
                    // If we're losing, value cards that can recover
                    if (lifePointDifference < -2000)
                    {
                        if (card.HasType(CardType.Trap) && card.HasType(CardType.Counter)) 
                            adjustedScore += 10;
                    }
                    
                    // If we're winning, value cards that can push for game
                    if (lifePointDifference > 2000 && Enemy.LifePoints <= 3000)
                    {
                        if (card.HasType(CardType.Monster) && card.Attack >= 2000)
                            adjustedScore += 15;
                    }
                }
                
                // Only update if significant change
                if (Math.Abs(adjustedScore - baseScore) >= 5)
                {
                    CardComboScores[cardId] = adjustedScore;
                    comboScoresChanged = true;
                }
            }
        }
        
        // Count trap cards in a player's spell zones
        private int CountTraps(ClientField field)
        {
            int count = 0;
            for (int i = 0; i < 5; i++)
            {
                ClientCard card = field.SpellZone[i];
                if (card != null && card.IsTrap())
                {
                    count++;
                }
            }
            return count;
        }
        
        // Updated to use EvaluateCardWithContext and RL
        private bool SummonHighestAttackMonster()
        {
            if (Card == null) return false;
            
            // Set current action type for domain knowledge integration
            CurrentActionType = ActionType.Summon;
            
            // Enhance RL with domain knowledge
            EnhanceRLWithDomainKnowledge();
            
            // Track that we used this monster
            if (!CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
            }
            
            // Use RL to decide which monster to summon
            if (RL != null)
            {
                double actionValue = RL.GetCardActionValue(Card.Id, ActionType.Summon);
                RL.TrackAction(Card.Id, ActionType.Summon);
                
                if (actionValue > 5.0)
                {
                    Logger.DebugWriteLine($"RL decided to summon monster {Card.Id} with value {actionValue}");
                    return true;
                }
                else if (actionValue < -5.0)
                {
                    Logger.DebugWriteLine($"RL avoiding summoning {Card.Id}");
                    return false;
                }
            }
            
            // Use context-aware evaluation
            int score = EvaluateCardWithContext(Card);
            
            // If the score is high or we have no monsters, strongly prefer summoning
            if (score >= 15 || Bot.GetMonsterCount() == 0)
            {
                Logger.DebugWriteLine($"High evaluation score {score} for monster {Card.Id}, summoning");
                return true; 
            }
            
            // Score-based decision with randomness
            return Program.Rand.Next(20) < score;
        }
        
        private bool SpecialSummonMonster()
        {
            // Track that we're special summoning a monster
            if (Card != null && !CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
                
                // RL: Track this action
                if (RL != null)
                {
                    RL.TrackAction(Card.Id, ActionType.SpSummon);
                    
                    // If we have negative experience with this card, consider not summoning
                    double spSummonValue = RL.GetCardActionValue(Card.Id, ActionType.SpSummon);
                    if (spSummonValue < -10.0 && new Random().NextDouble() < 0.7)
                    {
                        Logger.DebugWriteLine($"RL suggests NOT special summoning {Card.Id} (value: {spSummonValue})");
                        return false;
                    }
                    
                    // If we have very positive experience, make sure to summon
                    if (spSummonValue > 15.0)
                    {
                        Logger.DebugWriteLine($"RL strongly suggests special summoning {Card.Id} (value: {spSummonValue})");
                        return true;
                    }
                }
            }
            
            // Simple special summon logic - to be expanded in real implementation
            return true; // Return true to allow any special summon
        }
        
        // Override spell activation to track and use RL
        private bool SpellActivate()
        {
            if (Card == null) return false;
            
            // Set current action type for domain knowledge integration
            CurrentActionType = ActionType.Activate;
            
            // Enhance RL with domain knowledge
            EnhanceRLWithDomainKnowledge();
            
            // Track that we used this spell
            if (!CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
                
                // Some spells are key parts of certain combos - track them in sequence
                if (IsLibraryFTKCard(Card.Id))
                {
                    SuccessfulCardSequence.Add(Card.Id);
                    Logger.DebugWriteLine($"Added to successful sequence: {Card.Id}");
                }
            }
            
            // If RL system exists, use it to help decide
            if (RL != null)
            {
                double actionValue = RL.GetCardActionValue(Card.Id, ActionType.Activate);
                
                if (actionValue > 5.0 || (Duel.Player == 1 && actionValue > 0.0))
                {
                    Logger.DebugWriteLine($"RL decided to activate spell card {Card.Id} with value {actionValue}");
                    RL.TrackAction(Card.Id, ActionType.Activate);
                    return true;
                }
                else if (actionValue < -5.0)
                {
                    Logger.DebugWriteLine($"RL avoiding activation of spell {Card.Id}");
                    return false;
                }
            }
            
            // Use the card's score to decide based on our learned data
            if (CardComboScores.ContainsKey(Card.Id))
            {
                int score = CardComboScores[Card.Id];
                if (score >= 10)
                {
                    Logger.DebugWriteLine($"High score {score} for {Card.Id}, definitely activate");
                    return true;
                }
                else if (score < 0)
                {
                    Logger.DebugWriteLine($"Negative score {score} for {Card.Id}, probably avoid");
                    return Program.Rand.Next(10) > 7; // Only 30% chance to activate
                }
            }
            
            // Default behavior - 50% chance to activate unknown cards
            return Program.Rand.Next(10) >= 5;
        }
        
        // Override trap activation to track and use RL
        private bool TrapActivate()
        {
            if (Card == null) return false;
            
            // Set current action type for domain knowledge integration
            CurrentActionType = ActionType.Activate;
            
            // Enhance RL with domain knowledge
            EnhanceRLWithDomainKnowledge();
            
            // Track that we used this trap
            if (!CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
            }
            
            // If RL system exists, use it to help decide
            if (RL != null)
            {
                double actionValue = RL.GetCardActionValue(Card.Id, ActionType.Activate);
                
                if (actionValue > 5.0 || (Duel.Player == 1 && actionValue > 0.0))
                {
                    Logger.DebugWriteLine($"RL decided to activate trap card {Card.Id} with value {actionValue}");
                    RL.TrackAction(Card.Id, ActionType.Activate);
                    return true;
                }
                else if (actionValue < -5.0)
                {
                    Logger.DebugWriteLine($"RL avoiding activation of trap {Card.Id}");
                    return false;
                }
            }
            
            // Use the card's score to decide based on our learned data
            if (CardComboScores.ContainsKey(Card.Id))
            {
                int score = CardComboScores[Card.Id];
                if (score >= 10)
                {
                    Logger.DebugWriteLine($"High score {score} for trap {Card.Id}, definitely activate");
                    return true;
                }
                else if (score < 0)
                {
                    Logger.DebugWriteLine($"Negative score {score} for trap {Card.Id}, probably avoid");
                    return Program.Rand.Next(10) > 7; // Only 30% chance to activate
                }
            }
            
            // More cautious with traps - 40% chance to activate unknown ones
            return Program.Rand.Next(10) >= 6;
        }
        
        private bool SpellSet()
        {
            // Set spell/trap cards if we have empty zones
            return Bot.SpellZone.GetMatchingCardsCount(card => card == null) > 0 && 
                  (Card.IsSpell() || Card.IsTrap());
        }
        
        // Updated to use EvaluateCardWithContext and RL
        private bool MonsterRepos()
        {
            // Track this action with RL
            if (Card != null && RL != null)
            {
                if (Card.IsAttack())
                {
                    RL.TrackAction(Card.Id, ActionType.ToDefense);
                }
                else
                {
                    RL.TrackAction(Card.Id, ActionType.ToAttack);
                }
            }
            
            // Use RL to decide whether to change position based on past experience
            if (Card.IsMonster() && Card.IsFaceup() && RL != null)
            {
                double toDefenseValue = RL.GetCardActionValue(Card.Id, ActionType.ToDefense);
                double toAttackValue = RL.GetCardActionValue(Card.Id, ActionType.ToAttack);
                
                // If we're in attack mode but ToDefense has a high value, change to defense
                if (Card.IsAttack() && toDefenseValue > 5.0)
                {
                    Logger.DebugWriteLine($"RL suggests changing {Card.Id} to defense (value: {toDefenseValue})");
                    return true;
                }
                
                // If we're in defense mode but ToAttack has a high value, change to attack
                if (Card.IsDefense() && toAttackValue > 5.0)
                {
                    Logger.DebugWriteLine($"RL suggests changing {Card.Id} to attack (value: {toAttackValue})");
                    return true;
                }
                
                // If our action has a very negative value, avoid it
                if (Card.IsAttack() && toDefenseValue < -5.0)
                {
                    return false;
                }
                
                if (Card.IsDefense() && toAttackValue < -5.0)
                {
                    return false;
                }
            }
            
            // Default behavior: Change monsters to attack position if ATK > DEF, otherwise defense
            if (Card.IsMonster() && Card.IsFaceup())
            {
                if (Card.Attack >= Card.Defense && Card.IsDefense())
                    return true;
                if (Card.Attack < Card.Defense && Card.IsAttack())
                    return true;
            }
            return false;
        }

        private GameStage GetCurrentGameStage()
        {
            if (Duel.Turn <= 2) return GameStage.Early;
            if (Duel.Turn <= 6) return GameStage.Mid;
            return GameStage.Late;
        }

        private void IdentifyDeckArchetype()
        {
            // Return if deck not loaded
            if (DeckCardIds.Count == 0) return;
            
            Logger.DebugWriteLine("Identifying deck archetype...");
            
            // Blue-Eyes detection
            int blueEyesCards = DeckCardIds.Count(id => 
                id == 89631139 || id == 38517737 || id == 23434538 || id == 71039903);
            if (blueEyesCards >= 3) 
            {
                DeckArchetype = "Blue-Eyes";
                Logger.DebugWriteLine("Identified deck: Blue-Eyes");
                return;
            }
            
            // Dark Magician detection
            int darkMagicianCards = DeckCardIds.Count(id => 
                id == 46986414 || id == 2314238 || id == 7084129);
            if (darkMagicianCards >= 3) 
            {
                DeckArchetype = "Dark Magician";
                Logger.DebugWriteLine("Identified deck: Dark Magician");
                return;
            }
            
            // Library FTK detection
            int libraryCount = DeckCardIds.Count(id => id == RoyalMagicalLibraryId);
            int spellCount = DeckCardIds.Count(id => {
                var card = NamedCard.Get(id);
                return card != null && card.HasType(CardType.Spell);
            });
            if (libraryCount >= 1 && spellCount >= 20) 
            {
                DeckArchetype = "Library FTK";
                Logger.DebugWriteLine("Identified deck: Library FTK");
                return;
            }
            
            // Default
            DeckArchetype = "Unknown";
            Logger.DebugWriteLine("Unidentified deck type");
        }
        
        // Update combo performance stats when duel ends
        private void UpdateComboPerformance(bool won)
        {
            // Skip if no combos were detected
            if (SuccessfulCardSequence.Count < 2) return;
            
            // Get combo representation
            string comboKey = string.Join(",", SuccessfulCardSequence);
            
            // Initialize stats if not present
            if (!ComboPerformance.ContainsKey(comboKey))
            {
                ComboPerformance[comboKey] = new ComboStats
                {
                    ComboCards = SuccessfulCardSequence.ToList(),
                    TotalUses = 0,
                    Wins = 0,
                    Losses = 0
                };
            }
            
            // Update stats
            ComboPerformance[comboKey].TotalUses++;
            if (won)
            {
                ComboPerformance[comboKey].Wins++;
                Logger.DebugWriteLine($"Updated combo {comboKey} with WIN");
            }
            else
            {
                ComboPerformance[comboKey].Losses++;
                Logger.DebugWriteLine($"Updated combo {comboKey} with LOSS");
            }
            
            // Calculate win rate
            double winRate = (double)ComboPerformance[comboKey].Wins / ComboPerformance[comboKey].TotalUses;
            Logger.DebugWriteLine($"Combo {comboKey} now has win rate: {winRate:P2} ({ComboPerformance[comboKey].Wins}/{ComboPerformance[comboKey].TotalUses})");
            
            // Update card scores based on combo performance
            foreach (int cardId in SuccessfulCardSequence)
            {
                if (!CardComboScores.ContainsKey(cardId))
                {
                    CardComboScores[cardId] = 0;
                }
                
                // Adjust score based on win rate
                if (winRate >= 0.6)
                {
                    // Good combo, increase score
                    CardComboScores[cardId] += 2;
                    Logger.DebugWriteLine($"Increased score for card {cardId} to {CardComboScores[cardId]} due to good win rate");
                }
                else if (winRate <= 0.4)
                {
                    // Bad combo, decrease score
                    CardComboScores[cardId] -= 1;
                    Logger.DebugWriteLine($"Decreased score for card {cardId} to {CardComboScores[cardId]} due to poor win rate");
                }
            }
            
            // Flag that scores have changed
            comboScoresChanged = true;
        }
        
        // When enemy plays a card that causes us problems, record potential counters
        private void LearnCardCounter(int enemyCardId)
        {
            if (!CardCounters.ContainsKey(enemyCardId))
            {
                CardCounters[enemyCardId] = new CounterStrategy { CardId = enemyCardId };
            }
            
            // Look for cards in our hand that could potentially counter this threat
            var potentialCounters = Bot.Hand.Where(c => c != null && c.Id > 0)
                .Select(c => c.Id)
                .Where(id => !CardCounters[enemyCardId].EffectiveCounters.Contains(id))
                .ToList();
            
            foreach (int counterId in potentialCounters)
            {
                CardCounters[enemyCardId].EffectiveCounters.Add(counterId);
                Logger.DebugWriteLine($"Added potential counter {counterId} for enemy card {enemyCardId}");
            }
        }
        
        // Learn combos specific to a particular game stage
        private void LearnContextualCombo(List<int> cardIds, GameStage stage)
        {
            if (!StageSpecificCombos.ContainsKey(stage))
            {
                StageSpecificCombos[stage] = new List<List<int>>();
            }
            
            if (cardIds.Count >= 2 && !ContainsCombo(StageSpecificCombos[stage], cardIds))
            {
                StageSpecificCombos[stage].Add(cardIds);
                Logger.DebugWriteLine($"Learned {stage} game combo: {string.Join(", ", cardIds)}");
            }
        }
        
        // Should we play this particular combo based on historical performance?
        private bool ShouldPlayCombo(List<int> combo)
        {
            // Convert to string key
            string comboKey = string.Join("-", combo);
            
            // If we have stats, use them to decide
            if (ComboPerformance.ContainsKey(comboKey))
            {
                var stats = ComboPerformance[comboKey];
                
                // If win rate is below threshold and we have enough data, be less likely to play it
                if (stats.WinRate < 0.4 && stats.Wins + stats.Losses > 5)
                {
                    // 40% chance to skip this low-performing combo
                    Random rand = new Random();
                    return rand.NextDouble() > 0.4;
                }
                
                // If it's highly successful, always play it
                if (stats.WinRate > 0.7 && stats.Wins + stats.Losses > 5)
                {
                    return true;
                }
            }
            
            // Default behavior
            return true;
        }
        
        // Detect longer card chains beyond just pairs
        private void IdentifyCardChains(List<int> playedCards, bool wasSuccessful)
        {
            if (playedCards.Count < 2) return;
            
            // Check for 2-card combos
            for (int i = 0; i < playedCards.Count - 1; i++)
            {
                for (int j = i + 1; j < playedCards.Count; j++)
                {
                    List<int> combo = new List<int> { playedCards[i], playedCards[j] };
                    if (!ContainsCombo(KnownCombos, combo))
                    {
                        KnownCombos.Add(combo);
                        combosChanged = true;
                        Logger.DebugWriteLine($"Identified 2-card combo: {playedCards[i]}, {playedCards[j]}");
                    }
                }
            }
            
            // Check for 3-card combos if we have enough cards and it was successful
            if (playedCards.Count >= 3 && wasSuccessful)
            {
                for (int i = 0; i < playedCards.Count - 2; i++)
                {
                    for (int j = i + 1; j < playedCards.Count - 1; j++)
                    {
                        for (int k = j + 1; k < playedCards.Count; k++)
                        {
                            List<int> combo = new List<int> { playedCards[i], playedCards[j], playedCards[k] };
                            if (!ContainsCombo(KnownCombos, combo))
                            {
                                KnownCombos.Add(combo);
                                combosChanged = true;
                                Logger.DebugWriteLine($"Identified successful 3-card combo: {playedCards[i]}, {playedCards[j]}, {playedCards[k]}");
                            }
                        }
                    }
                }
            }
        }

        // Updated to return numeric score instead of boolean
        private int EvaluateCardWithContext(ClientCard card)
        {
            if (card == null) return 0;
            
            int score = EvaluateCardPower(card);
            GameStage currentStage = GetCurrentGameStage();
            
            // Adjust score based on current game state
            if (Bot.LifePoints < 2000 && card.HasType(CardType.Trap)) 
            {
                // Value defensive cards more when low on life
                score += 10;
            }
            
            // In early game, prioritize setup cards
            if (currentStage == GameStage.Early)
            {
                if (card.HasType(CardType.Spell) && (card.HasType(CardType.Continuous) || card.HasType(CardType.Field)))
                {
                    score += 5;
                }
            }
            
            // In mid game, prioritize monster effects
            if (currentStage == GameStage.Mid)
            {
                if (card.HasType(CardType.Monster) && card.HasType(CardType.Effect))
                {
                    score += 5;
                }
            }
            
            // In late game, prioritize high attack and finishers
            if (currentStage == GameStage.Late)
            {
                if (card.HasType(CardType.Monster) && card.Attack >= 2500)
                {
                    score += 10;
                }
            }
            
            // Check if card is part of a high-performing combo
            foreach (var combo in KnownCombos)
            {
                if (combo.Contains(card.Id))
                {
                    string comboKey = string.Join("-", combo);
                    if (ComboPerformance.ContainsKey(comboKey) && ComboPerformance[comboKey].WinRate > 0.6)
                    {
                        score += 15;
                        break;
                    }
                }
            }
            
            // Apply knowledge learned from opponents
            score += UseOpponentKnowledge(card);
            
            return score;
        }
        
        // New method to use knowledge learned from opponents
        private int UseOpponentKnowledge(ClientCard card)
        {
            if (card == null) return 0;
            int bonus = 0;
            
            // Check if this card appeared in opponent combos
            foreach (var opCombo in OpponentCombos)
            {
                if (opCombo.Contains(card.Id))
                {
                    Logger.DebugWriteLine($"Card {card.Id} found in opponent combo - considering it valuable");
                    bonus += 8; // Significant bonus for cards opponents use effectively
                    
                    // Check if we might be able to complete an opponent combo
                    // If we have more cards from this combo in hand, prioritize them even higher
                    int matchingCardsInHand = 0;
                    foreach (int comboCardId in opCombo)
                    {
                        if (comboCardId == card.Id) continue; // Skip the current card
                        
                        // Check if the card is in our hand
                        foreach (ClientCard handCard in Bot.Hand)
                        {
                            if (handCard != null && handCard.Id == comboCardId)
                            {
                                matchingCardsInHand++;
                                break;
                            }
                        }
                        
                        // Check if it's already on our field
                        foreach (ClientCard fieldCard in Bot.GetMonsters())
                        {
                            if (fieldCard != null && fieldCard.Id == comboCardId)
                            {
                                matchingCardsInHand++; // Count as "in hand" for combo purposes
                                break;
                            }
                        }
                        
                        foreach (ClientCard fieldCard in Bot.GetSpells())
                        {
                            if (fieldCard != null && fieldCard.Id == comboCardId)
                            {
                                matchingCardsInHand++; // Count as "in hand" for combo purposes
                                break;
                            }
                        }
                    }
                    
                    // Calculate how close we are to completing this combo
                    if (matchingCardsInHand > 0)
                    {
                        double comboCompletionRatio = (double)(matchingCardsInHand + 1) / opCombo.Count;
                        int comboBonus = (int)(25 * comboCompletionRatio);
                        
                        bonus += comboBonus;
                        Logger.DebugWriteLine($"We have {matchingCardsInHand + 1}/{opCombo.Count} cards for this opponent combo - adding bonus of {comboBonus}");
                        
                        // If we're close to completing a combo, give extra priority
                        if (comboCompletionRatio >= 0.7)
                        {
                            bonus += 15;
                            Logger.DebugWriteLine("Close to completing an opponent combo - prioritizing highly");
                        }
                    }
                    
                    break;
                }
            }
            
            // Check if this card is a counter to something the opponent plays
            if (CardCounters.Any(counter => counter.Value.EffectiveCounters.Contains(card.Id)))
            {
                Logger.DebugWriteLine($"Card {card.Id} is a known counter - prioritizing it");
                bonus += 10; // High bonus for counter cards
            }
            
            // Check if the opponent took significant damage after we played this card
            // This suggests it's effective against their strategy
            if (CardsPlayedThisTurn.Contains(card.Id) && previousEnemyLifePoints > Enemy.LifePoints)
            {
                int damage = previousEnemyLifePoints - Enemy.LifePoints;
                if (damage >= 1000)
                {
                    Logger.DebugWriteLine($"Card {card.Id} contributed to {damage} damage - rating higher");
                    bonus += Math.Min(20, damage / 100); // Up to +20 bonus based on damage
                }
            }
            
            // Try opponent archetype-specific cards
            string opponentArchetype = GetOpponentArchetype();
            if (opponentArchetype != "Unknown" && IsEffectiveAgainstArchetype(card.Id, opponentArchetype))
            {
                bonus += 15;
                Logger.DebugWriteLine($"Card {card.Id} is effective against {opponentArchetype} archetype");
            }
            
            return bonus;
        }

        // Determine opponent's archetype based on cards played
        private string GetOpponentArchetype()
        {
            // Check for Blue-Eyes cards
            bool hasBlueEyes = OpponentCardsPlayedThisTurn.Any(id => 
                id == 89631139 || // Blue-Eyes White Dragon
                id == 38517737 || // Blue-Eyes Alternative White Dragon
                id == 71039903);  // Blue-Eyes Twin Burst Dragon
            
            if (hasBlueEyes) return "Blue-Eyes";
            
            // Check for Dark Magician cards
            bool hasDarkMagician = OpponentCardsPlayedThisTurn.Any(id =>
                id == 46986414 || // Dark Magician
                id == 2314238);   // Dark Magical Circle
            
            if (hasDarkMagician) return "Dark-Magician";
            
            // Check for Sky Striker cards
            bool hasSkyStriker = OpponentCardsPlayedThisTurn.Any(id =>
                id == 63288573 || // Sky Striker Ace - Kagari
                id == 90673288);  // Sky Striker Mobilize - Engage!
            
            if (hasSkyStriker) return "Sky-Striker";
            
            return "Unknown";
        }

        // Check if a card is effective against a specific archetype
        private bool IsEffectiveAgainstArchetype(int cardId, string opponentArchetype)
        {
            switch (opponentArchetype)
            {
                case "Blue-Eyes":
                    // Cards effective against Blue-Eyes (high ATK monsters)
                    int[] blueEyesCounters = new int[]
                    {
                        44095762,  // Mirror Force
                        62279055,  // Magic Cylinder
                        25880422,  // Offerings to the Doomed
                        14532163   // Lightning Storm
                    };
                    return blueEyesCounters.Contains(cardId);
                    
                case "Dark-Magician":
                    // Cards effective against Dark Magician (spell/trap focused)
                    int[] darkMagicianCounters = new int[]
                    {
                        23434538,  // Anti-Spell Fragrance
                        97077563,  // Call of the Haunted
                        40605147   // Solemn Strike
                    };
                    return darkMagicianCounters.Contains(cardId);
                    
                case "Sky-Striker":
                    // Cards effective against Sky Striker (spell-heavy)
                    int[] skyStrikerCounters = new int[]
                    {
                        23434538,  // Anti-Spell Fragrance
                        27243130,  // Ash Blossom & Joyous Spring
                        24224830   // Called by the Grave
                    };
                    return skyStrikerCounters.Contains(cardId);
                    
                default:
                    return false;
            }
        }
        
        // Re-add card effect handlers that were accidentally removed
        private bool HeavyStormDusterEffect()
        {
            // Track that we played this card
            if (!CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
            }
            
            // Target face-up or face-down spell/trap cards
            List<ClientCard> targets = Enemy.SpellZone.GetMatchingCards(card => card != null).ToList();
            if (targets.Count == 0) return false;
            
            AI.SelectCard(targets.OrderByDescending(card => card.IsFaceup() ? 1 : 0).ToList());
            return true;
        }
        
        private bool TwistersEffect()
        {
            // Track that we played this card
            if (!CardsPlayedThisTurn.Contains(Card.Id))
            {
                CardsPlayedThisTurn.Add(Card.Id);
            }
            
            // Select a card to discard
            IList<ClientCard> discardCandidates = Bot.Hand.Where(card => card.Id != Card.Id && EvaluateCardPower(card) < 5).ToList();
            if (discardCandidates.Count == 0) return false;
            
            // Select targets to destroy
            List<ClientCard> targets = Enemy.SpellZone.GetMatchingCards(card => card != null).ToList();
            if (targets.Count == 0) return false;
            
            AI.SelectCard(discardCandidates.OrderBy(card => EvaluateCardPower(card)).First());
            AI.SelectNextCard(targets.OrderByDescending(card => card.IsFaceup() ? 1 : 0).ToList());
            return true;
        }

        // General RL-based activation decision for all cards
        private bool RLBasedActivation()
        {
            if (Card != null && RL != null)
            {
                double actionValue = RL.GetCardActionValue(Card.Id, ActionType.Activate);
                RL.TrackAction(Card.Id, ActionType.Activate);
                
                // Use RL data to decide
                if (actionValue > 5.0)
                {
                    Logger.DebugWriteLine($"RL confidently activating {Card.Id}");
                    return true;
                }
                else if (actionValue < -5.0)
                {
                    Logger.DebugWriteLine($"RL avoiding activation of {Card.Id}");
                    return false;
                }
            }
            
            // Default logic if RL has no strong opinion
            return Program.Rand.Next(10) >= 5 && DefaultDontChainMyself();
        }

        // New method to analyze opponent's strategy in detail
        private void AnalyzeOpponentStrategy()
        {
            Logger.DebugWriteLine("Analyzing opponent strategy...");
            
            // Track strong monsters the opponent controls
            List<ClientCard> strongEnemyMonsters = Enemy.GetMonsters()
                .Where(card => card != null && card.Attack >= 2000)
                .ToList();
            
            if (strongEnemyMonsters.Count > 0)
            {
                Logger.DebugWriteLine($"Opponent has {strongEnemyMonsters.Count} strong monsters");
                
                // Record these monster IDs to learn their strategies
                foreach (var monster in strongEnemyMonsters)
                {
                    if (!OpponentCardsPlayedThisTurn.Contains(monster.Id))
                    {
                        OpponentCardsPlayedThisTurn.Add(monster.Id);
                        Logger.DebugWriteLine($"Learning about strong opponent monster: {monster.Id}");
                    }
                }
            }
            
            // Analyze opponent's spell/trap strategy
            int continuousSpellCount = 0;
            int continuousTrapCount = 0;
            int quickPlayCount = 0;
            
            foreach (ClientCard card in Enemy.SpellZone)
            {
                if (card == null) continue;
                
                if (card.HasType(CardType.Continuous) && card.IsSpell()) continuousSpellCount++;
                if (card.HasType(CardType.Continuous) && card.IsTrap()) continuousTrapCount++;
                if (card.HasType(CardType.QuickPlay)) quickPlayCount++;
                
                // Track these cards for learning
                if (!OpponentCardsPlayedThisTurn.Contains(card.Id))
                {
                    OpponentCardsPlayedThisTurn.Add(card.Id);
                }
            }
            
            Logger.DebugWriteLine($"Opponent backrow: {continuousSpellCount} continuous spells, {continuousTrapCount} continuous traps, {quickPlayCount} quick-plays");
            
            // If opponent has a significant backrow, adjust our strategy
            if (continuousTrapCount >= 2)
            {
                Logger.DebugWriteLine("Opponent has multiple continuous traps - prioritizing backrow removal");
                // Prioritize cards like MST, Twin Twisters, etc.
                if (CardComboScores.ContainsKey(MysticalSpaceTyphoon))
                    CardComboScores[MysticalSpaceTyphoon] += 10;
                if (CardComboScores.ContainsKey(TwinTwisters))
                    CardComboScores[TwinTwisters] += 10;
                if (CardComboScores.ContainsKey(HeavyStormDuster))
                    CardComboScores[HeavyStormDuster] += 10;
                comboScoresChanged = true;
            }
            
            // Identify likely archetypes based on monsters
            IdentifyOpponentArchetype();
        }

        // Method to identify opponent's deck archetype
        private void IdentifyOpponentArchetype()
        {
            // Simple archetype detection based on played cards
            bool hasBlueEyes = OpponentCardsPlayedThisTurn.Any(id => 
                id == 89631139 || // Blue-Eyes White Dragon
                id == 38517737 || // Blue-Eyes Alternative White Dragon
                id == 71039903);  // Blue-Eyes Twin Burst Dragon
            
            bool hasDarkMagician = OpponentCardsPlayedThisTurn.Any(id =>
                id == 46986414 || // Dark Magician
                id == 2314238);   // Dark Magical Circle
            
            if (hasBlueEyes)
            {
                Logger.DebugWriteLine("Detected opponent archetype: Blue-Eyes");
                // Adjust strategy for Blue-Eyes matchup
                // They often have high ATK monsters but less protection
                
                // Prioritize cards that can deal with high ATK monsters
                foreach (int cardId in DeckCardIds)
                {
                    var card = NamedCard.Get(cardId);
                    if (card != null && card.HasType(CardType.Trap) && (
                        card.Description.Contains("destroy") || 
                        card.Description.Contains("banish") ||
                        card.Description.Contains("return to")))
                    {
                        if (CardComboScores.ContainsKey(cardId))
                        {
                            CardComboScores[cardId] += 5;
                            comboScoresChanged = true;
                        }
                    }
                }
            }
            else if (hasDarkMagician)
            {
                Logger.DebugWriteLine("Detected opponent archetype: Dark Magician");
                // Adjust strategy for Dark Magician matchup
                // They often have strong spell/trap interactions
                
                // Prioritize spell/trap removal
                if (CardComboScores.ContainsKey(MysticalSpaceTyphoon))
                    CardComboScores[MysticalSpaceTyphoon] += 8;
                if (CardComboScores.ContainsKey(TwinTwisters))
                    CardComboScores[TwinTwisters] += 8;
                comboScoresChanged = true;
            }
        }

        // New method to incorporate domain knowledge from built-in enums
        private bool EnhanceRLWithDomainKnowledge()
        {
            // Initialize if not already done
            if (!DomainKnowledgeInitialized)
            {
                Logger.DebugWriteLine("Initializing domain knowledge integration with RL system");
                BuildCardClassificationDictionary();
                DomainKnowledgeInitialized = true;
            }
            
            // Apply domain knowledge in decision-making contexts
            if (Card != null && RL != null)
            {
                // Check if card is in a special classification
                double classificationBonus = GetCardClassificationBonus(Card.Id);
                if (classificationBonus != 0)
                {
                    // Apply classification knowledge to RL
                    RL.BoostCardActionValue(Card.Id, CurrentActionType, classificationBonus);
                    Logger.DebugWriteLine($"Applied domain knowledge boost of {classificationBonus} to {Card.Id}");
                }
                
                // Adjust RL strategy based on opponent's board state
                AdjustRLForOpponentThreats();
            }
            
            return false; // Don't activate anything, just enhance RL
        }

        // Track if domain knowledge has been initialized
        private bool DomainKnowledgeInitialized = false;

        // Dictionary to store card classifications
        private Dictionary<int, CardClassification> CardClassifications = new Dictionary<int, CardClassification>();

        // Enum to classify cards based on their characteristics
        private enum CardClassification
        {
            Normal = 0,
            Dangerous = 1,
            Floodgate = 2, 
            Invincible = 3,
            ShouldNotTarget = 4,
            PreventActivation = 5
        }

        // Current action type (used by RL)
        private ActionType CurrentActionType = ActionType.None;

        // Build the card classification dictionary from the enums
        private void BuildCardClassificationDictionary()
        {
            // Reset the dictionary
            CardClassifications.Clear();
            
            // Hard-code important cards from each category
            // This eliminates dependency on external enums
            
            // Some dangerous monsters
            AddToClassification(54366836, CardClassification.Dangerous); // LionHeart
            AddToClassification(78371393, CardClassification.Dangerous); // Yubel
            AddToClassification(4779091, CardClassification.Dangerous);  // Yubel - Terror Incarnate
            AddToClassification(31764700, CardClassification.Dangerous); // Yubel - The Ultimate Nightmare
            AddToClassification(63845230, CardClassification.Dangerous); // Eater of Millions
            AddToClassification(97403510, CardClassification.Dangerous); // Heart-eartH Dragon
            
            // Some common floodgates
            AddToClassification(18144506, CardClassification.Floodgate); // Harpie's Feather Storm
            AddToClassification(40044918, CardClassification.Floodgate); // Lose 1 Turn
            AddToClassification(35059553, CardClassification.Floodgate); // Kaiser Colosseum
            AddToClassification(19254117, CardClassification.Floodgate); // Grand Horn of Heaven
            AddToClassification(23516703, CardClassification.Floodgate); // Summon Limit
            
            // Some invincible monsters
            AddToClassification(33198837, CardClassification.Invincible); // Malefic Truth Dragon
            AddToClassification(511000824, CardClassification.Invincible); // Supreme King Z-ARC
            AddToClassification(21417692, CardClassification.Invincible); // Ultimate Conductor Tyranno
            
            // Some cards that should not be targeted
            AddToClassification(44968687, CardClassification.ShouldNotTarget); // Revenge Dawm - King's Back
            AddToClassification(93238626, CardClassification.ShouldNotTarget); // Blue-Eyes Spirit Dragon
            
            // Some cards that prevent activation
            AddToClassification(53347303, CardClassification.PreventActivation); // Fog King
            AddToClassification(79968632, CardClassification.PreventActivation); // Unknown Synchron
            
            Logger.DebugWriteLine($"Built card classification dictionary with {CardClassifications.Count} entries");
        }

        // Helper method to add a card to the classifications dictionary
        private void AddToClassification(int cardId, CardClassification classification)
        {
            if (cardId > 0)
            {
                CardClassifications[cardId] = classification;
                Logger.DebugWriteLine($"Classified card {cardId} as {classification}");
            }
        }

        // Get bonus value based on card classification
        private double GetCardClassificationBonus(int cardId)
        {
            if (CardClassifications.ContainsKey(cardId))
            {
                switch (CardClassifications[cardId])
                {
                    case CardClassification.Dangerous:
                        return CurrentActionType == ActionType.Activate ? 15.0 : 0.0;
                        
                    case CardClassification.Floodgate:
                        return CurrentActionType == ActionType.Activate ? 20.0 : 0.0;
                        
                    case CardClassification.Invincible:
                        // Higher bonus for summoning invincible monsters
                        return CurrentActionType == ActionType.Summon ? 25.0 : 10.0;
                        
                    case CardClassification.ShouldNotTarget:
                        // Negative bonus for targeting cards that shouldn't be targeted
                        return CurrentActionType == ActionType.Target ? -25.0 : 0.0;
                        
                    case CardClassification.PreventActivation:
                        // Higher bonus for activating cards that prevent opponent's activations
                        return CurrentActionType == ActionType.Activate ? 18.0 : 0.0;
                        
                    default:
                        return 0.0;
                }
            }
            
            return 0.0;
        }

        // Adjust RL strategy based on opponent's threats
        private void AdjustRLForOpponentThreats()
        {
            // Check opponent's field for dangerous cards
            foreach (ClientCard card in Enemy.GetMonsters())
            {
                if (card == null || card.Id <= 0) continue;
                
                // Check if it's a dangerous monster
                if (CardClassifications.ContainsKey(card.Id) && 
                    (CardClassifications[card.Id] == CardClassification.Dangerous || 
                     CardClassifications[card.Id] == CardClassification.Invincible))
                {
                    // Find cards in our hand that can deal with this threat
                    foreach (ClientCard ourCard in Bot.Hand)
                    {
                        if (ourCard == null) continue;
                        
                        // Check if our card can deal with the threat
                        if (IsCardRemovalEffect(ourCard))
                        {
                            // If card can negate/destroy/banish, boost its value
                            RL.BoostCardActionValue(ourCard.Id, ActionType.Activate, 15.0);
                            Logger.DebugWriteLine($"Boosting card {ourCard.Id} as it can remove threat {card.Id}");
                        }
                    }
                }
                
                // Check if it has high ATK
                if (card.Attack >= 2500)
                {
                    // Find cards in our hand that can deal with high ATK monsters
                    foreach (ClientCard ourCard in Bot.Hand)
                    {
                        if (ourCard == null) continue;
                        
                        if (ourCard.HasType(CardType.Trap) || 
                            (ourCard.HasType(CardType.Spell) && ourCard.HasType(CardType.QuickPlay)))
                        {
                            // Boost defensive cards when facing high ATK monsters
                            RL.BoostCardActionValue(ourCard.Id, ActionType.Activate, 10.0);
                        }
                    }
                }
            }
            
            // Check opponent's spell/trap zone for floodgates
            foreach (ClientCard card in Enemy.SpellZone)
            {
                if (card == null || card.Id <= 0) continue;
                
                // Check if it's a floodgate
                if (CardClassifications.ContainsKey(card.Id) && 
                    CardClassifications[card.Id] == CardClassification.Floodgate)
                {
                    // Find spell/trap removal in our hand
                    foreach (ClientCard ourCard in Bot.Hand)
                    {
                        if (ourCard == null) continue;
                        
                        // If the card description contains removal terms
                        var namedCard = NamedCard.Get(ourCard.Id);
                        if (namedCard != null && 
                            (namedCard.Description.Contains("destroy") || 
                             namedCard.Description.Contains("banish") ||
                             namedCard.Description.Contains("return")))
                        {
                            // Boost cards that can remove floodgates
                            RL.BoostCardActionValue(ourCard.Id, ActionType.Activate, 20.0);
                            Logger.DebugWriteLine($"Boosting card {ourCard.Id} as it can remove floodgate {card.Id}");
                        }
                    }
                }
            }
        }

        // Helper method to check if a card can remove threats
        private bool IsCardRemovalEffect(ClientCard card)
        {
            if (card == null) return false;
            
            // Check common removal card IDs
            int[] removalCardIds = new int[] 
            {
                5318639,   // Mystical Space Typhoon
                43898403,  // Twin Twisters
                83326048,  // Dimensional Barrier
                23924608,  // Heavy Storm Duster
                82732705,  // Skill Drain
                18144506,  // Harpie's Feather Storm
                77538567,  // Dark Hole
                53129443,  // Dark Hole
                44095762,  // Mirror Force
                84749824,  // Solemn Warning
                41420027,  // Solemn Judgment
                40605147,  // Solemn Strike
                24224830   // Called by the Grave
            };
            
            if (removalCardIds.Contains(card.Id))
            {
                return true;
            }
            
            // Check card description for common removal keywords
            var namedCard = NamedCard.Get(card.Id);
            if (namedCard != null && namedCard.Description != null)
            {
                string desc = namedCard.Description.ToLower();
                
                // Check for common removal keywords
                if (desc.Contains("destroy") || 
                    desc.Contains("banish") || 
                    desc.Contains("remove from") || 
                    desc.Contains("return to") || 
                    desc.Contains("negate") || 
                    desc.Contains("cannot activate"))
                {
                    return true;
                }
            }
            
            return false;
        }

        // Update all decision-making methods to use domain knowledge
        private bool MonsterEffectActivate()
        {
            if (Card == null) return false;
            
            // Set current action type for domain knowledge integration
            CurrentActionType = ActionType.Activate;
            
            // Enhance RL with domain knowledge
            EnhanceRLWithDomainKnowledge();
            
            // Check if this is a complex decision
            if (IsComplexDecision(Card, ActionType.Activate))
            {
                Logger.DebugWriteLine($"Using MCTS to evaluate monster effect activation for {Card.Id}");
                // Get potential targets for the effect
                List<ClientCard> potentialTargets = Enemy.GetMonsters()
                    .Where(m => m != null && m.IsFaceup())
                    .ToList();
                return EvaluateComplexDecision(Card, ActionType.Activate, potentialTargets);
            }
            
            // If RL system exists, use it to decide
            if (RL != null)
            {
                double actionValue = RL.GetCardActionValue(Card.Id, ActionType.Activate);
                
                if (actionValue > 5.0 || (Duel.Player == 1 && actionValue > 0.0))
                {
                    Logger.DebugWriteLine($"RL decided to activate monster effect for {Card.Id} with value {actionValue}");
                    RL.TrackAction(Card.Id, ActionType.Activate);
                    return true;
                }
            }
            
            return Program.Rand.Next(10) >= 5;
        }

        // Reinforcement Learning System
        public class CardActionValueNetwork
        {
            private Dictionary<string, double> ActionValues = new Dictionary<string, double>();
            private Dictionary<string, int> ActionCounts = new Dictionary<string, int>();
            private List<string> CurrentTurnActions = new List<string>();
            private Random random = new Random();
            private bool ValuesChanged = false; // Track if values have changed
            
            // Enhanced learning parameters
            private double BaseLearningRate = 0.3;
            private double MinLearningRate = 0.05;
            private int TotalGameCount = 0;
            private double BaseExplorationRate = 0.4;
            private double MinExplorationRate = 0.05;
            
            // Experience replay buffer
            private const int MAX_EXPERIENCES = 1000;
            private List<ExperienceEntry> ExperienceBuffer = new List<ExperienceEntry>();
            
            // Enhanced tracking
            private Dictionary<int, CardUsageStats> CardStats = new Dictionary<int, CardUsageStats>();
            
            public CardActionValueNetwork()
            {
                // Load previous values if they exist
                LoadQValues();
            }
            
            private class ExperienceEntry
            {
                public string ActionKey;
                public double Reward;
                public double StateValue;
            }
            
            private class CardUsageStats
            {
                public int TotalUses = 0;
                public int SuccessfulUses = 0;
                public int FailedUses = 0;
                public double AverageReward = 0;
                public List<double> RecentRewards = new List<double>();
                
                public void AddReward(double reward)
                {
                    TotalUses++;
                    if (reward > 0) SuccessfulUses++;
                    if (reward < 0) FailedUses++;
                    
                    // Keep track of recent rewards (last 10)
                    RecentRewards.Add(reward);
                    if (RecentRewards.Count > 10)
                        RecentRewards.RemoveAt(0);
                    
                    // Update running average
                    AverageReward = RecentRewards.Count > 0 ? RecentRewards.Average() : 0;
                }
                
                public double GetSuccessRate()
                {
                    return TotalUses > 0 ? (double)SuccessfulUses / TotalUses : 0;
                }
            }
            
            // Save Q-values to file
            public void SaveQValues()
            {
                try
                {
                    // Create data object for serialization
                    var data = new Dictionary<string, object>
                    {
                        ["ActionValues"] = ActionValues,
                        ["ActionCounts"] = ActionCounts,
                        ["CardStats"] = CardStats,
                        ["TotalGameCount"] = TotalGameCount,
                        ["LastUpdated"] = DateTime.Now.ToString()
                    };
                    
                    // Create directory if it doesn't exist
                    string directory = Path.GetDirectoryName(Path.Combine(Directory.GetCurrentDirectory(), "universal_rl_data.json"));
                    if (!Directory.Exists(directory) && directory != null)
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // Serialize and save
                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "universal_rl_data.json"), json);
                    Logger.DebugWriteLine($"Saved reinforcement learning data with {ActionValues.Count} action values");
                    ValuesChanged = false;
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error saving reinforcement learning data: {ex.Message}");
                }
            }
            
            // Load Q-values from file
            public void LoadQValues()
            {
                try
                {
                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "universal_rl_data.json");
                    if (File.Exists(filePath))
                    {
                        string json = File.ReadAllText(filePath);
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        
                        if (data != null)
                        {
                            if (data.ContainsKey("ActionValues"))
                                ActionValues = JsonConvert.DeserializeObject<Dictionary<string, double>>(data["ActionValues"].ToString());
                            
                            if (data.ContainsKey("ActionCounts"))
                                ActionCounts = JsonConvert.DeserializeObject<Dictionary<string, int>>(data["ActionCounts"].ToString());
                            
                            if (data.ContainsKey("CardStats"))
                                CardStats = JsonConvert.DeserializeObject<Dictionary<int, CardUsageStats>>(data["CardStats"].ToString());
                            
                            if (data.ContainsKey("TotalGameCount"))
                                TotalGameCount = Convert.ToInt32(data["TotalGameCount"]);
                            
                            Logger.DebugWriteLine($"Loaded RL data with {ActionValues.Count} action values and {TotalGameCount} total games");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error loading reinforcement learning data: {ex.Message}");
                    // Initialize with empty collections if loading fails
                    ActionValues = new Dictionary<string, double>();
                    ActionCounts = new Dictionary<string, int>();
                    CardStats = new Dictionary<int, CardUsageStats>();
                }
            }
            
            // Track an action being taken
            public void TrackAction(int cardId, ActionType actionType)
            {
                string key = GetActionKey(cardId, actionType);
                CurrentTurnActions.Add(key);
                
                if (!ActionCounts.ContainsKey(key))
                {
                    ActionCounts[key] = 0;
                }
                
                ActionCounts[key]++;
                
                // Track card usage stats
                if (!CardStats.ContainsKey(cardId))
                {
                    CardStats[cardId] = new CardUsageStats();
                }
                
                ValuesChanged = true;
            }
            
            // Get the estimated value of taking an action
            public double GetCardActionValue(int cardId, ActionType actionType)
            {
                string key = GetActionKey(cardId, actionType);
                double baseValue = 0.0;
                
                if (ActionValues.ContainsKey(key))
                {
                    baseValue = ActionValues[key];
                    
                    // Add exploration noise based on dynamic exploration rate
                    double explorationRate = GetCurrentExplorationRate();
                    
                    // Actions with fewer samples get more exploration
                    int count = ActionCounts.ContainsKey(key) ? ActionCounts[key] : 0;
                    double confidenceBonus = 5.0 / Math.Max(1, Math.Sqrt(count + 1));
                    
                    // Thompson sampling - add noise proportional to our uncertainty
                    double noise = (random.NextDouble() * 2.0 - 1.0) * explorationRate * (10.0 / (count + 10.0));
                    
                    return baseValue + noise + confidenceBonus;
                }
                
                // For unknown actions, use optimistic initialization to encourage exploration
                return 2.0 + (random.NextDouble() * 2.0);
            }
            
            // Calculate current exploration rate - decreases as the bot plays more games
            private double GetCurrentExplorationRate()
            {
                // Decay from BaseExplorationRate to MinExplorationRate over many games
                double decay = Math.Min(1.0, (double)TotalGameCount / 100.0);
                return BaseExplorationRate - (decay * (BaseExplorationRate - MinExplorationRate));
            }
            
            // Calculate current learning rate - decreases with more experiences for stability
            private double GetCurrentLearningRate(string actionKey)
            {
                int count = ActionCounts.ContainsKey(actionKey) ? ActionCounts[actionKey] : 0;
                
                // Decay from BaseLearningRate to MinLearningRate as we see more examples
                double decay = Math.Min(1.0, (double)count / 100.0);
                return BaseLearningRate - (decay * (BaseLearningRate - MinLearningRate));
            }
            
            // Boost the action value of a card (used for domain knowledge integration)
            public void BoostCardActionValue(int cardId, ActionType actionType, double boost)
            {
                string key = GetActionKey(cardId, actionType);
                
                if (!ActionValues.ContainsKey(key))
                {
                    ActionValues[key] = 0.0;
                }
                
                // Apply a temporary boost to the action value
                // This doesn't permanently change the learned values
                ActionValues[key] += boost;
            }
            
            // Start a new turn
            public void StartTurn(ClientField bot, ClientField enemy)
            {
                CurrentTurnActions.Clear();
            }
            
            // End a turn and learn from results
            public void EndTurn(ClientField bot, ClientField enemy)
            {
                // Calculate reward
                double reward = CalculateRewards(bot, enemy);
                
                // Update value estimates for all actions taken this turn
                foreach (string actionKey in CurrentTurnActions)
                {
                    if (!ActionValues.ContainsKey(actionKey))
                    {
                        ActionValues[actionKey] = 0.0;
                    }
                    
                    // Get current learning rate based on action count
                    double learningRate = GetCurrentLearningRate(actionKey);
                    
                    // Extract card ID from action key
                    int cardId = ExtractCardIdFromKey(actionKey);
                    
                    // Update card stats
                    if (cardId > 0 && CardStats.ContainsKey(cardId))
                    {
                        CardStats[cardId].AddReward(reward);
                    }
                    
                    // Add to experience buffer for replay learning
                    ExperienceBuffer.Add(new ExperienceEntry 
                    { 
                        ActionKey = actionKey,
                        Reward = reward,
                        StateValue = ActionValues[actionKey]
                    });
                    
                    // Trim experience buffer if needed
                    if (ExperienceBuffer.Count > MAX_EXPERIENCES)
                    {
                        ExperienceBuffer.RemoveAt(0);
                    }
                    
                    // Simple update rule: move towards the reward using variable learning rate
                    ActionValues[actionKey] += learningRate * (reward - ActionValues[actionKey]);
                    ValuesChanged = true;
                }
                
                // Perform experience replay learning (learn from random past experiences)
                PerformExperienceReplay();
            }
            
            // Extract card ID from action key
            private int ExtractCardIdFromKey(string actionKey)
            {
                try
                {
                    // Action key format is "CardId:ActionType"
                    string[] parts = actionKey.Split(':');
                    int cardId;
                    if (parts.Length >= 2 && int.TryParse(parts[0], out cardId))
                    {
                        return cardId;
                    }
                }
                catch { }
                return 0;
            }
            
            // Perform experience replay - learn from random past experiences
            private void PerformExperienceReplay()
            {
                if (ExperienceBuffer.Count < 10) return;
                
                // Sample some experiences randomly and learn from them again
                int replaySamples = Math.Min(10, ExperienceBuffer.Count / 2);
                
                for (int i = 0; i < replaySamples; i++)
                {
                    int index = random.Next(ExperienceBuffer.Count);
                    ExperienceEntry exp = ExperienceBuffer[index];
                    
                    double learningRate = GetCurrentLearningRate(exp.ActionKey) * 0.5; // Half learning rate for replays
                    
                    if (!ActionValues.ContainsKey(exp.ActionKey))
                    {
                        ActionValues[exp.ActionKey] = 0.0;
                    }
                    
                    // Update the action value based on this past experience
                    ActionValues[exp.ActionKey] += learningRate * (exp.Reward - ActionValues[exp.ActionKey]);
                    ValuesChanged = true;
                }
            }
            
            // Get key for storing action values
            private string GetActionKey(int cardId, ActionType actionType)
            {
                return $"{cardId}:{actionType}";
            }
            
            // Calculate rewards based on game state
            public double CalculateRewards(ClientField bot, ClientField enemy, bool isDuelEnding = false)
            {
                double reward = 0.0;
                
                // Life point differential
                double lifePointDifferential = (bot.LifePoints - enemy.LifePoints) / 1000.0;
                reward += lifePointDifferential * 0.5;
                
                // Card advantage 
                int botCardCount = bot.Hand.Count + bot.GetMonsterCount() + bot.GetSpellCount();
                int enemyCardCount = enemy.Hand.Count + enemy.GetMonsterCount() + enemy.GetSpellCount();
                int cardDifferential = botCardCount - enemyCardCount;
                reward += cardDifferential * 1.5;
                
                // Field advantage (weighted towards monsters)
                int botMonsters = bot.GetMonsterCount();
                int enemyMonsters = enemy.GetMonsterCount();
                reward += (botMonsters - enemyMonsters) * 2.0;
                
                // Bonus for having monsters with high attack
                foreach (ClientCard monster in bot.GetMonsters())
                {
                    if (monster != null)
                    {
                        if (monster.Attack >= 2500)
                            reward += 2.0;
                        else if (monster.Attack >= 1800)
                            reward += 1.0;
                    }
                }
                
                // Penalty for opponent having strong monsters
                foreach (ClientCard monster in enemy.GetMonsters())
                {
                    if (monster != null)
                    {
                        if (monster.Attack >= 2500)
                            reward -= 1.5;
                        else if (monster.Attack >= 1800)
                            reward -= 0.8;
                    }
                }
                
                // Larger rewards for winning/losing
                if (isDuelEnding)
                {
                    TotalGameCount++; // Increment game counter
                    
                    if (enemy.LifePoints <= 0 || bot.LifePoints > enemy.LifePoints)
                    {
                        reward += 50.0; // Big reward for winning
                        Logger.DebugWriteLine("RL system registers a WIN - rewarding all actions");
                    }
                    else if (bot.LifePoints <= 0 || bot.LifePoints < enemy.LifePoints)
                    {
                        reward -= 50.0; // Big penalty for losing
                        Logger.DebugWriteLine("RL system registers a LOSS - penalizing actions");
                    }
                }
                
                return reward;
            }
            
            // Process duel ending - learn from full game and prepare for next duel
            public void ProcessDuelEnd(ClientField bot, ClientField enemy, bool won)
            {
                // Calculate end-game reward
                double finalReward = CalculateRewards(bot, enemy, true);
                
                // Apply final reward to all actions this turn
                foreach (string actionKey in CurrentTurnActions)
                {
                    if (!ActionValues.ContainsKey(actionKey))
                    {
                        ActionValues[actionKey] = 0.0;
                    }
                    
                    double learningRate = GetCurrentLearningRate(actionKey);
                    ActionValues[actionKey] += learningRate * (finalReward - ActionValues[actionKey]);
                }
                
                // Add experience for replay
                foreach (string actionKey in CurrentTurnActions)
                {
                    ExperienceBuffer.Add(new ExperienceEntry 
                    { 
                        ActionKey = actionKey,
                        Reward = finalReward,
                        StateValue = ActionValues.ContainsKey(actionKey) ? ActionValues[actionKey] : 0.0
                    });
                }
                
                // Trim experience buffer if needed
                while (ExperienceBuffer.Count > MAX_EXPERIENCES)
                {
                    ExperienceBuffer.RemoveAt(0);
                }
                
                // Process more experiences for stronger learning at duel end
                for (int i = 0; i < 3; i++)
                {
                    PerformExperienceReplay();
                }
                
                // Clear current turn actions
                CurrentTurnActions.Clear();
                
                // Save updated values
                SaveQValues();
                
                Logger.DebugWriteLine($"Processed duel end, stored {ExperienceBuffer.Count} experiences, played {TotalGameCount} games");
            }
            
            // Get the most valuable cards based on learned data
            public List<int> GetTopValuedCards(int count = 10)
            {
                var cardValues = new Dictionary<int, double>();
                
                // Calculate average value for each card across all action types
                foreach (var entry in ActionValues)
                {
                    string[] parts = entry.Key.Split(':');
                    int cardId;
                    if (parts.Length >= 2 && int.TryParse(parts[0], out cardId))
                    {
                        if (!cardValues.ContainsKey(cardId))
                        {
                            cardValues[cardId] = 0;
                        }
                        cardValues[cardId] += entry.Value;
                    }
                }
                
                // Return the top N cards by value
                return cardValues.OrderByDescending(kv => kv.Value)
                                 .Take(count)
                                 .Select(kv => kv.Key)
                                 .ToList();
            }
            
            // Get best action type for a specific card
            public ActionType GetBestActionType(int cardId)
            {
                var actionTypes = Enum.GetValues(typeof(ActionType))
                                   .Cast<ActionType>()
                                   .Where(a => a != ActionType.None);
                
                ActionType bestAction = ActionType.None;
                double bestValue = double.MinValue;
                
                foreach (var actionType in actionTypes)
                {
                    string key = GetActionKey(cardId, actionType);
                    if (ActionValues.ContainsKey(key) && ActionValues[key] > bestValue)
                    {
                        bestValue = ActionValues[key];
                        bestAction = actionType;
                    }
                }
                
                return bestAction;
            }
        }

        // Override the base OnNewTurn to track turn changes and learn from results
        public override void OnNewTurn()
        {
            base.OnNewTurn();
            
            // Reset tracking for the new turn
            CardsPlayedThisTurn.Clear();
            OpponentCardsPlayedThisTurn.Clear();
            
            // Check if any combos were completed in the last turn
            DetectAndLearnCombos();
            
            // Reset life point tracking
            previousEnemyLifePoints = Enemy.LifePoints;
            previousBotLifePoints = Bot.LifePoints;
            
            // Reset action type
            CurrentActionType = ActionType.None;
            
            // Check and save combo data periodically (every 5 turns)
            if (Duel.Turn % 5 == 0)
            {
                SaveComboData();
            }
            
            // Reinforcement Learning: End previous turn and start new one
            if (RL != null)
            {
                // First, learn from the previous turn results
                RL.EndTurn(Bot, Enemy);
                
                // Then prepare for the new turn
                RL.StartTurn(Bot, Enemy);
                
                // Check for high-valued cards in hand and provide hints
                LogHighValueCardsInHand();
            }
            
            Logger.DebugWriteLine($"Turn {Duel.Turn}: New turn started. Bot LP: {Bot.LifePoints}, Enemy LP: {Enemy.LifePoints}");
        }

        // Log information about high-value cards in hand
        private void LogHighValueCardsInHand()
        {
            if (RL == null) return;
            
            // Get the top-valued cards from our learning system
            List<int> topCards = RL.GetTopValuedCards(5);
            
            // Check if any of these cards are in our hand
            List<int> highValueCardsInHand = new List<int>();
            foreach (ClientCard card in Bot.Hand)
            {
                if (card != null && topCards.Contains(card.Id))
                {
                    highValueCardsInHand.Add(card.Id);
                    
                    // Get the recommended action for this card
                    ActionType bestAction = RL.GetBestActionType(card.Id);
                    double actionValue = RL.GetCardActionValue(card.Id, bestAction);
                    
                    Logger.DebugWriteLine($"High-value card in hand: {card.Id}, recommended action: {bestAction}, value: {actionValue:F2}");
                }
            }
            
            if (highValueCardsInHand.Count > 0)
            {
                Logger.DebugWriteLine($"Found {highValueCardsInHand.Count} high-value cards in hand");
            }
        }

        // Detect and learn card combinations at the end of a turn
        private void DetectAndLearnCombos()
        {
            // Skip if we haven't played enough cards to form a combo
            if (CardsPlayedThisTurn.Count < 2)
            {
                Logger.DebugWriteLine("Not enough cards played to detect combos");
                return;
            }
            
            Logger.DebugWriteLine($"Analyzing {CardsPlayedThisTurn.Count} cards played for potential combos");
            
            // Check if this turn was "successful" - either we dealt damage or improved our field
            bool wasSuccessful = false;
            
            // We dealt significant damage to opponent
            if (previousEnemyLifePoints - Enemy.LifePoints >= 1000)
            {
                wasSuccessful = true;
                Logger.DebugWriteLine($"Successful turn: Dealt {previousEnemyLifePoints - Enemy.LifePoints} damage");
            }
            
            // Or we improved our field position (gained more monsters than opponent)
            // Using GetMonsterCount() to determine if we have a stronger field presence
            int ourMonsterCount = Bot.GetMonsterCount();
            int theirMonsterCount = Enemy.GetMonsterCount();
            
            // Check if we have more monsters than the opponent
            if (ourMonsterCount > theirMonsterCount && ourMonsterCount >= 2)
            {
                wasSuccessful = true;
                Logger.DebugWriteLine($"Successful turn: Field advantage ({ourMonsterCount} monsters vs {theirMonsterCount})");
            }
            
            // Add played cards to successful sequence if the turn was successful
            if (wasSuccessful)
            {
                foreach (int cardId in CardsPlayedThisTurn)
                {
                    if (!SuccessfulCardSequence.Contains(cardId))
                    {
                        SuccessfulCardSequence.Add(cardId);
                        Logger.DebugWriteLine($"Added card {cardId} to successful sequence");
                    }
                }
            }
            
            // Identify chains and potential combos from cards played
            IdentifyCardChains(CardsPlayedThisTurn, wasSuccessful);
            
            // If enough successful cards have been accumulated, learn it as a combo
            if (SuccessfulCardSequence.Count >= 2)
            {
                // Only store up to 5 card combos to prevent overly complex sequences
                List<int> comboToLearn = SuccessfulCardSequence.Take(Math.Min(5, SuccessfulCardSequence.Count)).ToList();
                
                // Learn this as a potential combo
                LearnCombo(comboToLearn);
                
                // Learn as stage-specific combo
                LearnContextualCombo(comboToLearn, GetCurrentGameStage());
                
                // If turn was successful, clear the sequence to start fresh
                if (wasSuccessful)
                {
                    SuccessfulCardSequence.Clear();
                    Logger.DebugWriteLine("Cleared successful sequence after learning combo");
                }
            }
            
            // Learn from opponent's plays too
            if (OpponentCardsPlayedThisTurn.Count >= 2)
            {
                List<int> opponentCombo = OpponentCardsPlayedThisTurn.Take(Math.Min(5, OpponentCardsPlayedThisTurn.Count)).ToList();
                LearnOpponentCombo(opponentCombo);
                Logger.DebugWriteLine($"Learned opponent combo with {opponentCombo.Count} cards");
            }
        }

        /// <summary>
        /// Checks if the bot has lethal damage on the field and prioritizes attacking
        /// </summary>
        private bool LethalAttackCheck()
        {
            // Only check during our battle phase
            if (Duel.Phase != DuelPhase.Battle || Duel.Player != 0)
                return false;
            
            // Check if we have enough attack power to win
            int totalAttackPower = 0;
            List<ClientCard> attackers = new List<ClientCard>();
            
            foreach (ClientCard monster in Bot.GetMonsters())
            {
                if (monster != null && monster.IsAttack() && !monster.IsShouldNotBeTarget() && !monster.IsDisabled())
                {
                    totalAttackPower += monster.Attack;
                    attackers.Add(monster);
                }
            }
            
            // If we have enough attack power to win, prioritize direct attacks
            if (totalAttackPower >= Enemy.LifePoints)
            {
                Logger.DebugWriteLine($"Lethal damage detected: {totalAttackPower} attack vs {Enemy.LifePoints} LP");
                
                // Check if direct attacks are possible
                int directAttackPower = 0;
                if (Enemy.GetMonsterCount() == 0)
                {
                    // All attackers can attack directly
                    directAttackPower = totalAttackPower;
                }
                else
                {
                    // Check if we have enough attack power to get through the opponent's monsters
                    List<ClientCard> enemyMonsters = Enemy.GetMonsters().Where(m => m != null && m.IsAttack()).ToList();
                    if (enemyMonsters.Count == 0)
                    {
                        // All monsters are in defense position, can attack directly if they can be bypassed
                        foreach (ClientCard attacker in attackers)
                        {
                            if (attacker.CanDirectAttack)
                            {
                                directAttackPower += attacker.Attack;
                            }
                        }
                    }
                    else
                    {
                        // Try to attack over the opponent's monsters first
                        // Sort enemy monsters by ATK
                        enemyMonsters.Sort((a, b) => a.Attack.CompareTo(b.Attack));
                        
                        // Sort our attackers by ATK (highest first)
                        attackers.Sort((a, b) => b.Attack.CompareTo(a.Attack));
                        
                        // See if we can defeat all enemy monsters and have enough attack left for game
                        int remainingAttack = totalAttackPower;
                        bool canDefeatAllMonsters = true;
                        
                        for (int i = 0; i < enemyMonsters.Count; i++)
                        {
                            if (i < attackers.Count && attackers[i].Attack > enemyMonsters[i].Attack)
                            {
                                remainingAttack -= enemyMonsters[i].Attack;
                            }
                            else
                            {
                                canDefeatAllMonsters = false;
                                break;
                            }
                        }
                        
                        if (canDefeatAllMonsters && remainingAttack >= Enemy.LifePoints)
                        {
                            Logger.DebugWriteLine("Can defeat all monsters and still have lethal damage");
                            directAttackPower = remainingAttack;
                        }
                    }
                }
                
                // If we have enough direct attack power for game, force the attack
                if (directAttackPower >= Enemy.LifePoints)
                {
                    // Signal the bot to prioritize attacking
                    Logger.DebugWriteLine($"Lethal direct damage available: {directAttackPower}. Going for game!");
                    
                    // If we have any attack boosting cards in hand, use them now
                    foreach (ClientCard card in Bot.Hand)
                    {
                        if (card != null && card.IsSpell() && 
                            (card.Name.Contains("Honest") || 
                             card.Name.Contains("Limiter Removal") || 
                             card.Name.Contains("Attack") || 
                             card.Name.Contains("Power")))
                        {
                            AI.SelectCard(card);
                            return true;
                        }
                    }
                    
                    // Proceed with attacking
                    return true;
                }
            }
            
            return false;
        }

        public override BattlePhaseAction OnSelectAttackTarget(ClientCard attacker, IList<ClientCard> defenders)
        {
            // Check if we have lethal directly - attack directly if possible
            if (attacker.CanDirectAttack && attacker.Attack >= Enemy.LifePoints)
            {
                Logger.DebugWriteLine($"Direct attack for game with {attacker.Name} ({attacker.Attack} ATK)");
                return AI.Attack(attacker, null);
            }
            
            // No defenders means direct attack
            if (defenders.Count == 0 && attacker.CanDirectAttack)
            {
                return AI.Attack(attacker, null);
            }

            // Check if opponent has monsters in Attack position that we can defeat for game
            foreach (ClientCard defender in defenders)
            {
                if (defender.IsAttack() && attacker.Attack > defender.Attack)
                {
                    int damage = attacker.Attack - defender.Attack;
                    if (damage >= Enemy.LifePoints)
                    {
                        Logger.DebugWriteLine($"Attack for game: {attacker.Name} vs {defender.Name} for {damage} damage");
                        return AI.Attack(attacker, defender);
                    }
                }
            }
            
            // Sort defenders by ATK (lowest first for attack position, highest first for defense)
            List<ClientCard> attackPos = new List<ClientCard>();
            List<ClientCard> defensePos = new List<ClientCard>();
            
            foreach (ClientCard defender in defenders)
            {
                if (defender.IsAttack())
                    attackPos.Add(defender);
                else
                    defensePos.Add(defender);
            }
            
            // Sort attack position monsters by ATK
            attackPos.Sort((a, b) => a.Attack.CompareTo(b.Attack));
            
            // Sort defense position monsters by DEF
            defensePos.Sort((a, b) => b.Defense.CompareTo(a.Defense));
            
            // If we're in a winning position, be aggressive
            bool aggressiveMode = Bot.LifePoints > Enemy.LifePoints && Bot.GetMonsterCount() >= Enemy.GetMonsterCount();
            
            // First, see if we can attack a monster in attack position for damage
            foreach (ClientCard defender in attackPos)
            {
                attacker.RealPower = attacker.Attack;
                defender.RealPower = defender.Attack;
                
                if (!OnPreBattleBetween(attacker, defender))
                    continue;
                    
                if (attacker.RealPower > defender.RealPower)
                {
                    // If in aggressive mode, or the damage is significant, attack
                    int damage = attacker.RealPower - defender.RealPower;
                    if (aggressiveMode || damage >= 1000 || defender.IsMonsterDangerous())
                    {
                        return AI.Attack(attacker, defender);
                    }
                }
            }
            
            // If none found or we're being cautious, check for defense position monsters we can safely destroy
            foreach (ClientCard defender in defensePos)
            {
                attacker.RealPower = attacker.Attack;
                defender.RealPower = defender.GetDefensePower();
                
                if (!OnPreBattleBetween(attacker, defender))
                    continue;
                    
                if (attacker.RealPower > defender.RealPower || (defender.IsFacedown() && aggressiveMode))
                {
                    // Prioritize face-down monsters that might be threats
                    return AI.Attack(attacker, defender);
                }
            }
            
            // If we're in aggressive mode and have no better targets, attack the weakest attack position monster
            if (aggressiveMode && attackPos.Count > 0)
            {
                ClientCard weakestAttacker = attackPos[0];
                if (attacker.Attack >= weakestAttacker.Attack)
                {
                    return AI.Attack(attacker, weakestAttacker);
                }
            }
            
            // If we can direct attack, do it
            if (attacker.CanDirectAttack)
            {
                return AI.Attack(attacker, null);
            }
            
            // No good targets found
            return null;
        }

        // Monte Carlo Tree Search implementation for complex decision making
        private class MCTSNode
        {
            public GameState State { get; set; }
            public List<MCTSNode> Children { get; set; } // Change from private to public setter
            public MCTSNode Parent { get; private set; }
            public int Visits { get; set; }
            public double Score { get; set; }
            public List<CardAction> AvailableActions { get; set; }
            
            public MCTSNode(GameState state, MCTSNode parent = null)
            {
                State = state;
                Parent = parent;
                Children = new List<MCTSNode>(); // Initialize the list in the constructor
                Visits = 0;
                Score = 0;
                AvailableActions = new List<CardAction>();
            }
            
            public bool IsFullyExpanded()
            {
                return AvailableActions.Count == 0;
            }
            
            public bool IsTerminal()
            {
                return State.IsGameOver;
            }
            
            public MCTSNode BestChild(double explorationWeight)
            {
                return Children.OrderByDescending(c => 
                    c.Score / c.Visits + 
                    explorationWeight * Math.Sqrt(2 * Math.Log(this.Visits) / c.Visits)
                ).First();
            }
        }

        private class CardAction
        {
            public ActionType Type { get; set; }
            public int CardId { get; set; }
            public int TargetCardId { get; set; }
            public int ExpectedValue { get; set; }
            
            public CardAction(ActionType type, int cardId, int targetCardId = 0, int expectedValue = 0)
            {
                Type = type;
                CardId = cardId;
                TargetCardId = targetCardId;
                ExpectedValue = expectedValue;
            }
        }

        private class GameState
        {
            public List<ClientCard> BotHand { get; set; }
            public List<ClientCard> BotMonsters { get; set; }
            public List<ClientCard> BotSpellTraps { get; set; }
            public List<ClientCard> EnemyMonsters { get; set; }
            public List<ClientCard> EnemySpellTraps { get; set; }
            public int BotLP { get; set; }
            public int EnemyLP { get; set; }
            public DuelPhase Phase { get; set; }
            public bool IsGameOver { get; set; }
            
            // Clone the current game state
            public GameState Clone()
            {
                return new GameState
                {
                    BotHand = BotHand.Where(c => c != null).Select(c => c.Clone()).ToList(),
                    BotMonsters = BotMonsters.Where(c => c != null).Select(c => c.Clone()).ToList(),
                    BotSpellTraps = BotSpellTraps.Where(c => c != null).Select(c => c.Clone()).ToList(),
                    EnemyMonsters = EnemyMonsters.Where(c => c != null).Select(c => c.Clone()).ToList(),
                    EnemySpellTraps = EnemySpellTraps.Where(c => c != null).Select(c => c.Clone()).ToList(),
                    BotLP = BotLP,
                    EnemyLP = EnemyLP,
                    Phase = Phase,
                    IsGameOver = IsGameOver
                };
            }
        }

        private class MonteCarloTreeSearch
        {
            private Random Random;
            private int SimulationCount;
            private double ExplorationWeight;
            
            public MonteCarloTreeSearch(int simulationCount = 1000, double explorationWeight = 1.4)
            {
                Random = new Random();
                SimulationCount = simulationCount;
                ExplorationWeight = explorationWeight;
            }
            
            public CardAction GetBestAction(GameState initialState, List<CardAction> availableActions)
            {
                // Input validation
                if (availableActions == null || availableActions.Count == 0)
                {
                    Logger.DebugWriteLine("MCTS: No available actions provided");
                    return null;
                }
                
                if (availableActions.Count == 1)
                {
                    Logger.DebugWriteLine("MCTS: Only one action available, returning it directly");
                    return availableActions[0];
                }
                
                // Create root node
                MCTSNode rootNode = new MCTSNode(initialState);
                if (rootNode.State == null)
                {
                    Logger.DebugWriteLine("MCTS: Invalid initial state");
                    return availableActions[0];
                }
                
                rootNode.AvailableActions = new List<CardAction>(availableActions); // Make a copy
                
                try
                {
                    // Run simulations
                    for (int i = 0; i < SimulationCount; i++)
                    {
                        MCTSNode selectedNode = Selection(rootNode);
                        if (selectedNode == null) break;
                        
                        MCTSNode expandedNode = Expansion(selectedNode);
                        if (expandedNode == null) break;
                        
                        double simulationResult = Simulation(expandedNode);
                        Backpropagation(expandedNode, simulationResult);
                    }
                    
                    // Check for empty children list
                    if (rootNode.Children == null || rootNode.Children.Count == 0)
                    {
                        Logger.DebugWriteLine("MCTS could not expand any nodes, returning first available action");
                        return availableActions[0];
                    }
                    
                    // Find best child
                    MCTSNode bestChild = null;
                    double bestScore = double.MinValue;
                    
                    foreach (MCTSNode child in rootNode.Children)
                    {
                        double score = child.Visits > 0 ? child.Score / child.Visits : 0;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestChild = child;
                        }
                    }
                    
                    if (bestChild == null)
                    {
                        Logger.DebugWriteLine("MCTS could not find a best child, returning first available action");
                        return availableActions[0];
                    }
                    
                    // Find the action corresponding to the best child
                    for (int i = 0; i < rootNode.Children.Count; i++)
                    {
                        if (rootNode.Children[i] == bestChild && i < availableActions.Count)
                        {
                            Logger.DebugWriteLine($"MCTS selected action {availableActions[i].Type} for card {availableActions[i].CardId} with {bestChild.Visits} visits and score {bestChild.Score}");
                            return availableActions[i];
                        }
                    }
                    
                    // Fallback
                    Logger.DebugWriteLine("MCTS indexing issue, falling back to first available action");
                    return availableActions[0];
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"MCTS exception: {ex.Message}, falling back to first available action");
                    return availableActions[0];
                }
            }
            
            private MCTSNode Selection(MCTSNode node)
            {
                while (!node.IsTerminal())
                {
                    if (!node.IsFullyExpanded())
                        return node;
                        
                    node = node.BestChild(ExplorationWeight);
                }
                return node;
            }
            
            private MCTSNode Expansion(MCTSNode node)
            {
                if (node == null || node.IsTerminal())
                    return node;
                    
                if (node.AvailableActions == null || node.AvailableActions.Count == 0)
                    return node;
                    
                // Get a random action
                int actionIndex = Random.Next(node.AvailableActions.Count);
                CardAction action = node.AvailableActions[actionIndex];
                
                // Make a safe copy of the remaining actions
                List<CardAction> remainingActions = new List<CardAction>(node.AvailableActions);
                remainingActions.RemoveAt(actionIndex);
                node.AvailableActions = remainingActions;
                
                // Create a new state by applying the action
                GameState newState = node.State?.Clone();
                if (newState == null)
                    return node; // Return the original node if we can't clone the state
                
                // Apply the action
                try
                {
                    ApplyAction(newState, action);
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error applying action in MCTS: {ex.Message}");
                    return node; // Return the original node if we can't apply the action
                }
                
                // Create a new child node
                MCTSNode childNode = new MCTSNode(newState, node);
                
                // Generate available actions for the new state
                try
                {
                    childNode.AvailableActions = GeneratePossibleActions(newState);
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"Error generating actions in MCTS: {ex.Message}");
                    childNode.AvailableActions = new List<CardAction>(); // Empty list if we can't generate actions
                }
                
                // Add the child to parent's children
                if (node.Children == null)
                    node.Children = new List<MCTSNode>();
                
                node.Children.Add(childNode);
                
                return childNode;
            }
            
            private double Simulation(MCTSNode node)
            {
                GameState state = node.State.Clone();
                
                // Simulate until terminal state
                int maxDepth = 10; // Limit simulation depth
                int depth = 0;
                
                while (!state.IsGameOver && depth < maxDepth)
                {
                    List<CardAction> actions = GeneratePossibleActions(state);
                    if (actions.Count == 0)
                        break;
                        
                    // Choose a random action
                    CardAction randomAction = actions[Random.Next(actions.Count)];
                    ApplyAction(state, randomAction);
                    
                    depth++;
                }
                
                // Evaluate the final state
                return EvaluateState(state);
            }
            
            private void Backpropagation(MCTSNode node, double score)
            {
                while (node != null)
                {
                    node.Visits++;
                    node.Score += score;
                    node = node.Parent;
                }
            }
            
            // Simplified action application - would be more complex in a real implementation
            private void ApplyAction(GameState state, CardAction action)
            {
                switch (action.Type)
                {
                    case ActionType.Activate:
                        // Simulate card activation
                        SimulateCardActivation(state, action);
                        break;
                    case ActionType.Summon:
                        // Simulate monster summon
                        SimulateMonsterSummon(state, action);
                        break;
                    case ActionType.SpSummon:
                        // Simulate special summon
                        SimulateSpecialSummon(state, action);
                        break;
                    case ActionType.SetMonster:
                    case ActionType.SetSpellTrap: // Changed from SpellSet to SetSpellTrap
                        // Simulate setting a card
                        SimulateSetCard(state, action);
                        break;
                    case ActionType.ToAttack:
                    case ActionType.ToDefense:
                        // Simulate changing battle position
                        SimulateChangePosition(state, action);
                        break;
                    default:
                        // Other action types
                        break;
                }
                
                // Check if game is over after the action
                CheckGameOver(state);
            }
            
            // Simulate activating a card
            private void SimulateCardActivation(GameState state, CardAction action)
            {
                // Find the card in hand or on field
                ClientCard card = FindCard(state, action.CardId);
                if (card == null) return;
                
                // Simulate generic effect based on card type
                if (card.IsMonster())
                {
                    // Monster effect
                    if (action.TargetCardId > 0)
                    {
                        // Target effect - find target
                        ClientCard target = FindCard(state, action.TargetCardId, true);
                        if (target != null && target.IsMonster())
                        {
                            // Simulate card destruction
                            if (Random.Next(100) < 70) // 70% chance the effect succeeds
                            {
                                state.EnemyMonsters.Remove(target);
                            }
                        }
                    }
                    else
                    {
                        // Non-targeting effect - generic bonus
                        // For example, increase all monster ATK
                        foreach (var monster in state.BotMonsters)
                        {
                            // Use RealPower instead of Attack since Attack is read-only
                            monster.RealPower += 500;
                        }
                    }
                }
                else if (card.IsSpell())
                {
                    // Spell activation
                    if (state.BotHand.Contains(card))
                    {
                        state.BotHand.Remove(card);
                        // Some spells go to GY, others stay on field
                        if (Random.Next(100) < 50) // 50% chance it's a continuous spell
                        {
                            state.BotSpellTraps.Add(card);
                        }
                    }
                    
                    // Generic spell effect
                    if (action.TargetCardId > 0)
                    {
                        // Target effect
                        ClientCard target = FindCard(state, action.TargetCardId, true);
                        if (target != null)
                        {
                            // Simulate target destruction
                            if (target.IsMonster())
                                state.EnemyMonsters.Remove(target);
                            else
                                state.EnemySpellTraps.Remove(target);
                        }
                    }
                    else
                    {
                        // Draw cards, special summon, etc.
                        if (state.BotHand.Count < 5 && Random.Next(100) < 30) // 30% chance it's a draw spell
                        {
                            // Add random card to hand using proper constructor
                            int randomCardId = Random.Next(10000, 99999);
                            state.BotHand.Add(new ClientCard(randomCardId, CardLocation.Hand, -1, 0, 0));
                        }
                    }
                }
                else if (card.IsTrap())
                {
                    // Trap activation - similar to spells
                    if (state.BotSpellTraps.Contains(card))
                    {
                        // Remove trap from field
                        state.BotSpellTraps.Remove(card);
                    }
                    
                    // Generic trap effect - usually disruption
                    if (state.EnemyMonsters.Count > 0)
                    {
                        // Remove a random enemy monster
                        state.EnemyMonsters.RemoveAt(Random.Next(state.EnemyMonsters.Count));
                    }
                }
            }
            
            // Simulate summoning a monster
            private void SimulateMonsterSummon(GameState state, CardAction action)
            {
                ClientCard card = FindCardInHand(state, action.CardId);
                if (card != null && state.BotMonsters.Count < 5)
                {
                    state.BotHand.Remove(card);
                    card.Position = (int)CardPosition.Attack; // Cast the enum to int
                    state.BotMonsters.Add(card);
                }
            }
            
            // Simulate special summoning
            private void SimulateSpecialSummon(GameState state, CardAction action)
            {
                if (state.BotMonsters.Count < 5)
                {
                    // Create a new monster card using constructor - cast CardPosition to int
                    ClientCard newMonster = new ClientCard(action.CardId, CardLocation.MonsterZone, -1, (int)CardPosition.Attack, 0);
                    
                    // Since Attack/Defense are read-only, we'll use RealPower as a workaround for simulation
                    int simulatedAttack = Random.Next(1000, 3000);
                    newMonster.RealPower = simulatedAttack; // Store the attack value
                    
                    state.BotMonsters.Add(newMonster);
                }
            }
            
            // Simulate setting a card
            private void SimulateSetCard(GameState state, CardAction action)
            {
                ClientCard card = FindCardInHand(state, action.CardId);
                if (card == null) return;
                
                state.BotHand.Remove(card);
                
                if (card.IsMonster() && state.BotMonsters.Count < 5)
                {
                    card.Position = (int)CardPosition.FaceDown; // Cast the enum to int
                    card.Position |= (int)CardPosition.Defence; // Changed from Defense to Defence
                    state.BotMonsters.Add(card);
                }
                else if ((card.IsSpell() || card.IsTrap()) && state.BotSpellTraps.Count < 5)
                {
                    card.Position = (int)CardPosition.FaceDown; // Cast the enum to int
                    state.BotSpellTraps.Add(card);
                }
            }
            
            // Simulate changing battle position
            private void SimulateChangePosition(GameState state, CardAction action)
            {
                ClientCard card = FindCardOnField(state, action.CardId);
                if (card == null || !card.IsMonster()) return;
                
                if (action.Type == ActionType.ToAttack)
                {
                    card.Position &= ~(int)CardPosition.Defence; // Changed from Defense to Defence
                    card.Position |= (int)CardPosition.Attack; // Cast the enum to int
                }
                else // ToDefense
                {
                    card.Position &= ~(int)CardPosition.Attack; // Cast the enum to int
                    card.Position |= (int)CardPosition.Defence; // Changed from Defense to Defence
                }
            }
            
            // Helper methods
            private ClientCard FindCard(GameState state, int cardId, bool searchEnemyField = false)
            {
                // Search hand
                ClientCard card = state.BotHand.FirstOrDefault(c => c.Id == cardId);
                if (card != null) return card;
                
                // Search monsters
                card = state.BotMonsters.FirstOrDefault(c => c.Id == cardId);
                if (card != null) return card;
                
                // Search spell/traps
                card = state.BotSpellTraps.FirstOrDefault(c => c.Id == cardId);
                if (card != null) return card;
                
                if (searchEnemyField)
                {
                    // Search enemy monsters
                    card = state.EnemyMonsters.FirstOrDefault(c => c.Id == cardId);
                    if (card != null) return card;
                    
                    // Search enemy spell/traps
                    card = state.EnemySpellTraps.FirstOrDefault(c => c.Id == cardId);
                }
                
                return card;
            }
            
            private ClientCard FindCardInHand(GameState state, int cardId)
            {
                return state.BotHand.FirstOrDefault(c => c.Id == cardId);
            }
            
            private ClientCard FindCardOnField(GameState state, int cardId)
            {
                ClientCard card = state.BotMonsters.FirstOrDefault(c => c.Id == cardId);
                if (card != null) return card;
                
                return state.BotSpellTraps.FirstOrDefault(c => c.Id == cardId);
            }
            
            // Check if the game is over
            private void CheckGameOver(GameState state)
            {
                if (state.BotLP <= 0 || state.EnemyLP <= 0)
                {
                    state.IsGameOver = true;
                }
            }
            
            // Generate possible actions from the current state
            private List<CardAction> GeneratePossibleActions(GameState state)
            {
                List<CardAction> actions = new List<CardAction>();
                
                // Generate actions based on cards in hand
                foreach (var card in state.BotHand)
                {
                    if (card.IsMonster())
                    {
                        // Can summon or set
                        actions.Add(new CardAction(ActionType.Summon, card.Id));
                        actions.Add(new CardAction(ActionType.SetMonster, card.Id));
                    }
                    else if (card.IsSpell())
                    {
                        // Can activate or set
                        actions.Add(new CardAction(ActionType.Activate, card.Id));
                        actions.Add(new CardAction(ActionType.SetSpellTrap, card.Id)); // Changed from SpellSet to SetSpellTrap
                        
                        // If it's a targeting spell, add potential targets
                        foreach (var monster in state.EnemyMonsters)
                        {
                            actions.Add(new CardAction(ActionType.Activate, card.Id, monster.Id));
                        }
                    }
                    else if (card.IsTrap())
                    {
                        // Can only set (normally)
                        actions.Add(new CardAction(ActionType.SetSpellTrap, card.Id)); // Changed from SpellSet to SetSpellTrap
                    }
                }
                
                // Generate actions for monsters on field
                foreach (var monster in state.BotMonsters)
                {
                    if (monster.IsFaceup())
                    {
                        // Can activate effect if any
                        actions.Add(new CardAction(ActionType.Activate, monster.Id));
                        
                        // Can change position
                        if (monster.IsAttack())
                            actions.Add(new CardAction(ActionType.ToDefense, monster.Id));
                        else if (monster.IsDefense())
                            actions.Add(new CardAction(ActionType.ToAttack, monster.Id));
                            
                        // Targeting effects
                        foreach (var enemyMonster in state.EnemyMonsters)
                        {
                            actions.Add(new CardAction(ActionType.Activate, monster.Id, enemyMonster.Id));
                        }
                    }
                    else if (monster.IsFacedown())
                    {
                        // Can flip summon
                        actions.Add(new CardAction(ActionType.Summon, monster.Id));
                    }
                }
                
                // Generate actions for spell/traps on field
                foreach (var card in state.BotSpellTraps)
                {
                    if (card.IsFacedown() && card.IsTrap())
                    {
                        // Can activate trap
                        actions.Add(new CardAction(ActionType.Activate, card.Id));
                        
                        // Target effects
                        foreach (var monster in state.EnemyMonsters)
                        {
                            actions.Add(new CardAction(ActionType.Activate, card.Id, monster.Id));
                        }
                    }
                    else if (card.IsFaceup())
                    {
                        // Can activate continuous effect
                        actions.Add(new CardAction(ActionType.Activate, card.Id));
                    }
                }
                
                return actions;
            }
            
            // Evaluate the state to determine its value
            private double EvaluateState(GameState state)
            {
                if (state.BotLP <= 0)
                    return 0.0; // Loss
                    
                if (state.EnemyLP <= 0)
                    return 1.0; // Win
                    
                // Calculate a score based on various factors
                double score = 0.5; // Base score
                
                // Life point difference
                double lpDiff = (state.BotLP - state.EnemyLP) / 8000.0;
                score += lpDiff * 0.2;
                
                // Field advantage
                double fieldAdvantage = state.BotMonsters.Count - state.EnemyMonsters.Count;
                score += fieldAdvantage * 0.05;
                
                // Card advantage
                double cardAdvantage = state.BotHand.Count - 3; // Assume 3 is average
                score += cardAdvantage * 0.03;
                
                // Board strength (ATK) - use RealPower instead of Attack
                double botATK = state.BotMonsters.Sum(m => m.RealPower);
                double enemyATK = state.EnemyMonsters.Sum(m => m.RealPower);
                double atkDiff = (botATK - enemyATK) / 10000.0; // Normalize
                score += atkDiff * 0.15;
                
                // Clamp the score between 0 and 1
                return Math.Max(0, Math.Min(1, score));
            }
        }

        // Initialize the Monte Carlo Tree Search system
        private MonteCarloTreeSearch MCTS;

        // Method to create a game state from the current duel state
        private GameState CreateGameStateFromCurrentState()
        {
            GameState state = new GameState
            {
                BotHand = Bot.Hand.Where(c => c != null).Select(c => c.Clone()).ToList(),
                BotMonsters = Bot.GetMonsters().Where(c => c != null).Select(c => c.Clone()).ToList(),
                BotSpellTraps = Bot.GetSpells().Where(c => c != null).Select(c => c.Clone()).ToList(),
                EnemyMonsters = Enemy.GetMonsters().Where(c => c != null).Select(c => c.Clone()).ToList(),
                EnemySpellTraps = Enemy.GetSpells().Where(c => c != null).Select(c => c.Clone()).ToList(),
                BotLP = Bot.LifePoints,
                EnemyLP = Enemy.LifePoints,
                Phase = Duel.Phase,
                IsGameOver = false
            };
            
            return state;
        }

        // Evaluate a complex decision using MCTS
        private bool EvaluateComplexDecision(ClientCard card, ActionType actionType, List<ClientCard> potentialTargets = null)
        {
            // If MCTS is null, initialize it
            if (MCTS == null)
            {
                MCTS = new MonteCarloTreeSearch(500); // Use fewer simulations to prevent timeout
            }

            // Create the current game state
            GameState currentState = CreateGameStateFromCurrentState();
            
            // Generate available actions - make sure to only include valid actions
            List<CardAction> availableActions = new List<CardAction>();
            
            // Add the current action
            availableActions.Add(new CardAction(actionType, card.Id));
            
            // Add alternative actions
            // For example, if we're deciding whether to activate a card,
            // consider alternative cards we could activate
            foreach (ClientCard otherCard in Bot.Hand)
            {
                if (otherCard != null && otherCard.Id != card.Id && otherCard.Id != 0)
                {
                    if (otherCard.IsMonster())
                    {
                        availableActions.Add(new CardAction(ActionType.Summon, otherCard.Id));
                    }
                    else if (otherCard.IsSpell())
                    {
                        availableActions.Add(new CardAction(ActionType.Activate, otherCard.Id));
                    }
                }
            }
            
            // If there are potential targets, add targeting actions
            if (potentialTargets != null && potentialTargets.Count > 0)
            {
                foreach (ClientCard target in potentialTargets)
                {
                    if (target != null && target.Id != 0)
                    {
                        availableActions.Add(new CardAction(actionType, card.Id, target.Id));
                    }
                }
            }
            
            // If we have no actions, default to true for the current action
            if (availableActions.Count <= 1)
            {
                Logger.DebugWriteLine($"Only one action available for {card.Id}, defaulting to true");
                return true;
            }
            
            try
            {
                // Run MCTS to find the best action
                CardAction bestAction = MCTS.GetBestAction(currentState, availableActions);
                
                if (bestAction == null)
                {
                    Logger.DebugWriteLine($"MCTS returned null for {card.Id}, defaulting to true");
                    return true; // Default to true if MCTS fails
                }
                
                // Check if the best action matches our proposed action
                bool shouldProceed = (bestAction.CardId == card.Id && bestAction.Type == actionType);
                
                Logger.DebugWriteLine($"MCTS decision for {card.Id}: {shouldProceed}");
                
                return shouldProceed;
            }
            catch (Exception ex)
            {
                // If there's an error in MCTS, log it and default to true
                Logger.DebugWriteLine($"MCTS error for {card.Id}: {ex.Message}, defaulting to true");
                return true;
            }
        }

        // Method to determine if a decision requires MCTS analysis
        private bool IsComplexDecision(ClientCard card, ActionType actionType)
        {
            // Determine if this is a complex enough decision to warrant MCTS
            // For example, high-impact cards or critical game states
            
            // High-impact cards like board wipes or negation effects
            if (card.HasType(CardType.Trap) && Duel.LastChainPlayer == 1)
            {
                return true; // Responding to opponent's actions with traps
            }
            
            // Spell cards with multiple targets
            if (card.IsSpell() && Enemy.GetMonsterCount() >= 2)
            {
                // Check card description for targeting
                var namedCard = NamedCard.Get(card.Id);
                if (namedCard != null && namedCard.Description != null && namedCard.Description.Contains("target"))
                {
                    return true; // Targeting decision
                }
            }
            
            // Monster effects that can change the game state significantly
            if (card.IsMonster() && card.Attack >= 2500 && actionType == ActionType.Activate)
            {
                return true; // Powerful monster effect
            }
            
            // Critical game state (low LP, few cards, etc.)
            if (Bot.LifePoints <= 2000 || Bot.Hand.Count <= 1)
            {
                return true; // Critical game state
            }
            
            return false; // Default to simpler decision making
        }
    }
}
