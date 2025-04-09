# WindBot Ignite

A fork of [IceYGO's WindBot](https://github.com/IceYGO/windbot), ported to the
[Project Ignis: EDOPro](https://github.com/edo9300/edopro) network protocol.

WindBot Ignite is a deterministic artificial intelligence that connects as a
virtual player to the YGOPro room system. Decks for this bot player **must** be
specifically prepared and compiled as individual executors.

## Features
- Written in C# targeting .NET Framework 4
- Compatible with Visual Studio 2015 or newer
- Supports multiple deck types and strategies
- Deterministic AI behavior
- Integration with Project Ignis: EDOPro
- Advanced AI features including:
  - Reinforcement Learning system
  - Monte Carlo Tree Search
  - Combo learning and optimization
  - Card classification and threat assessment
  - Adaptive strategy based on opponent's deck
  - Experience replay and learning from past duels

## Available Decks
* ABC
* Altergeist
* Blue-Eyes
* Blue-Eyes Ritual
* Burn
* Chain Burn
* Cyberse
* Dark Magician
* Dragma
* Dragunity
* Dragun of Red-Eyes
* Frog
* Gren Maju Stun
* Horus
* Kashtira
* Lightsworn Shaddoll Dino
* Mathmech
* Normal Monster Mash
* Normal Monster Mash II
* Orcust
* Qliphort
* R5NK
* Rainbow
* Rose Scrap Synchro
* Salamangreat
* Sky Striker
* Thunder Dragon
* Tearlaments
* Time Thief
* Toadally Awesome
* Trickstar
* Windwitch Gusto
* Witchcrafter Grass
* Yosenju
* ZEXAL Weapon
* Zoodiac
* Universal (Advanced AI with learning capabilities)

## Getting Started

### Prerequisites
- Visual Studio 2015 or newer
- .NET Framework 4
- Project Ignis: EDOPro

### Installation
1. Clone the repository
2. Open the solution in Visual Studio
3. Build the project
4. Configure your deck executors

## AI Features

### Universal Executor
The Universal Executor is an advanced AI system that includes:

- **Reinforcement Learning**: Adapts strategies based on duel outcomes
- **Monte Carlo Tree Search**: Evaluates complex game states and decisions
- **Combo Learning**: Identifies and optimizes card combinations
- **Threat Assessment**: Classifies cards and evaluates opponent threats
- **Adaptive Strategy**: Adjusts playstyle based on opponent's deck archetype
- **Experience Replay**: Learns from past duels to improve future performance

### Learning System
The AI continuously improves through:
- Card action value tracking
- Combo performance analysis
- Opponent strategy recognition
- Game stage-specific decision making
- Card classification and counter strategies

## Using the Universal Executor

The Universal Executor is an advanced AI system that can adapt to different decks and playstyles through reinforcement learning. Here's how to configure and use it:

### Configuration Files

The Universal Executor uses several JSON files for configuration and learning:

1. **universal_rl_qvalues.json** - Contains the Q-values for card actions, which determine the AI's decision making
2. **universal_combos.json** - Stores identified card combinations and sequences
3. **universal_combo_scores.json** - Tracks performance data for different card combinations
4. **universal_rewards_config.json** - Optional configuration for reward parameters and learning settings

These files are automatically created when the AI first runs and will be updated as the AI learns from duels.

### Action Types

The AI tracks different action types for each card to determine optimal play strategies:

- **Summon** - Normal summon a monster card
- **SpSummon/SpecialSummon** - Special summon a monster
- **Set** - Generic set action (parent of SetMonster and SetSpellTrap)
- **SetMonster** - Set a monster in face-down defense position
- **SetSpellTrap** - Set a spell or trap card face-down
- **Activate** - Activate a card effect
- **ToDefense/ToAttack** - Change monster battle position
- **Attack** - Attack with a monster
- **Target** - Select targets for effects
- **Tribute** - Use a monster as tribute

### Q-Values Explanation

The Q-values in universal_rl_qvalues.json represent the AI's learned preferences:

- Positive values (especially > 5.0) strongly encourage an action
- Negative values (especially < -5.0) discourage an action
- Values near zero indicate neutral or unexplored actions

Example format:
```json
{
  "LastUpdated": "date",
  "QValues": [
    {
      "Key": 12345678, // Card ID
      "Value": [
        {"Key": "Summon", "Value": 3.5},
        {"Key": "SetMonster", "Value": -2.1},
        {"Key": "ToDefense", "Value": 4.2}
      ]
    }
  ]
}
```

### Reward System

The Universal Executor uses a sophisticated reward system to evaluate and learn from game actions:

#### Reward Factors

Rewards are calculated based on multiple game state factors:
- **LifePointDifferential** - Difference between player and opponent life points
- **CardAdvantage** - Card count advantage compared to opponent
- **MonsterCountAdvantage** - Field presence advantage
- **HighAttackMonsterBonus** - Bonus for having powerful monsters on the field
- **GameWinReward** - Large positive reward for winning duels
- **GameLossReward** - Large negative reward for losing duels

The numerical values represent importance multipliers:
- Values < 1.0 (e.g., 0.5) reduce the importance of that factor
- Values = 1.0 represent standard/neutral importance
- Values > 1.0 (e.g., 2.0) increase the importance of that factor

For example, with the default values, having more monsters on the field (2.0) is considered four times more important than life point differences (0.5) when evaluating actions.

#### Reward Multipliers

The system also uses multipliers to prioritize certain card types and actions:
- **Card Type Rewards** - Different values for Monster (1.0), Spell (0.8), and Trap (0.7) cards
- **Action Type Multipliers** - Weighted values for different actions (Summon: 1.0, SpecialSummon: 1.2, etc.)

#### How Rewards Affect Learning

1. When actions lead to positive game state changes, those card-action pairs receive positive rewards
2. Actions with consistently high rewards will have their Q-values increase over time
3. The AI will gradually prefer actions that historically led to favorable outcomes
4. Different game stages (early, mid, late) may yield different rewards for the same action

#### Customizing Rewards

You can customize the reward system by creating or editing the universal_rewards_config.json file:

```json
{
  "RewardFactors": {
    "LifePointDifferential": 0.5,
    "CardAdvantage": 1.5,
    "MonsterCountAdvantage": 2.0,
    "HighAttackMonsterBonus": 0.8,
    "GameWinReward": 100.0,
    "GameLossReward": -100.0
  },
  "CardTypeRewards": {
    "Monster": 1.0,
    "Spell": 0.8,
    "Trap": 0.7
  },
  "ActionTypeMultipliers": {
    "Summon": 1.0,
    "SpecialSummon": 1.2,
    "Set": 0.7,
    "Activate": 1.0
  }
}
```

Adjusting these values will influence how the AI evaluates different actions and card types.

### Customizing the AI

To influence the AI's behavior:
1. Edit the universal_rl_qvalues.json file to adjust Q-values for specific cards
2. Create universal_rewards_config.json to customize reward factors, exploration rate, and learning parameters
3. Configure the exploration rate to balance between exploration and exploitation

### Common Use Cases

- **General Purpose AI**: Use as-is with any deck - it will learn over time
- **Specialized Deck AI**: Pre-seed the Q-values for your deck's key cards
- **Testing Opponent**: Configure aggressive or defensive behavior by modifying action type rewards

### Performance Analysis

After multiple duels, you can analyze the AI's performance:
- Check which cards have the highest Q-values for different actions
- Identify successful combos from universal_combo_scores.json
- Review card success rates and average rewards

### Technical Implementation

The Universal Executor uses a combination of:
- Q-learning for reinforcement learning (main decision making)
- Monte Carlo Tree Search for complex decision evaluation (depth 3-4 turns)
- Pattern recognition for combo identification and learning
- Domain-specific knowledge for card classifications and threat assessment

### Learning Process and File Persistence

The Universal Executor's learning process is persistent across sessions:

#### How Learning Persists

1. **Automatic Saving** - The AI automatically saves its learning data to the JSON files after each duel
2. **Incremental Learning** - Each duel builds upon previous experience, gradually refining the AI's strategy
3. **Long-term Improvement** - The more duels played, the more refined the AI's decision-making becomes
4. **Game Stage Adaptation** - The AI develops different strategies for early, mid, and late game stages

#### Managing Learning Data

You can manage the AI's learning data in several ways:

- **Backup Learning Files** - Copy the JSON files to preserve a particularly well-trained AI
- **Reset Learning** - Delete or rename the JSON files to start fresh (new files will be created automatically)
- **Transfer Learning** - Move JSON files between installations to share learned strategies
- **Edit Q-Values** - Manually adjust values to guide the AI in specific directions

#### Recommended Learning Approach

For optimal results:
1. Let the AI play at least 20-30 duels to develop basic understanding
2. Observe which cards consistently receive high Q-values
3. Back up the files before making manual adjustments
4. If performance degrades after changes, restore the backup and try different adjustments

#### Learning Limitations

The current implementation has some limitations to be aware of:
- Learning is tied to specific card IDs, not card effects or categories
- The AI may develop biases based on the decks it has faced most frequently
- Extremely complex card interactions may not be fully learned without many examples
- Early decisions have a stronger impact on learning than late-game decisions

#### Future Improvements

To address these limitations, future versions could implement:

- **Card Categorization** - Group similar cards together for transferable learning (e.g., all "destruction" effects)
- **Effect-Based Learning** - Associate learning with card effects rather than specific card IDs
- **Meta-Learning** - Implement higher-level learning to recognize deck archetypes and optimal counter-strategies
- **Memory Prioritization** - Weight learning experiences by their impact on the duel outcome
- **Advanced Neural Networks** - Replace or supplement Q-learning with deep neural networks for more complex pattern recognition
- **Guided Learning** - Allow manual specification of card interactions and combos to accelerate learning
- **Simulated Experience** - Generate synthetic duels to train on rare scenarios and edge cases
- **Opponent Modeling** - Build models of opponent behavior to improve prediction of likely responses

Users can contribute to improving the AI by:
- Reporting patterns of suboptimal play
- Contributing pre-trained QValue files for specific deck archetypes
- Testing and benchmarking against various opponent strategies
- Developing specialized reward systems for particular playstyles

## Contributing

We welcome pull requests for fixes and new additions! Please note that it might take some time for them to be evaluated due to our current workload.

### Guidelines
- Please report bugs on Discord for verification
- For new additions, add code files to both WindBot and libWindbot projects
- Focus testing on the WindBot project

## Architecture

### Key Changes from Upstream
- Merged with [libWindbot](https://github.com/mercury233/libWindbot) for Android aar support
- Improved repository structure
- Experimental ExecutorBase feature for loading additional executors from DLLs
- Advanced AI system with learning capabilities

### libWindbot Compilation Requirements
- Windows environment
- Visual Studio 2017 or 2019
- Android development workloads (Xamarin and native)
- 32-bit Mono SDK
- Android SDK Platform 24 (Android 7.0)
- NDK r15c

## License

WindBot Ignite is free/libre and open source software licensed under the GNU
Affero General Public License, version 3 or later. Please see
[LICENSE](https://github.com/ProjectIgnis/windbot/blob/master/LICENSE) and
[COPYING](https://github.com/ProjectIgnis/windbot/blob/master/COPYING) for more
details.
