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
