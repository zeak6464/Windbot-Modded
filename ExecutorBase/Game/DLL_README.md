# Yu-Gi-Oh! DLL Functions

This file provides wrapper functions for common operations in the Yu-Gi-Oh! game engine. These functions mimic the API of the original game DLL, making it easier to integrate existing code or write new AI routines.

## Card Information Functions

- `DLL_CardGetAtk(card)`: Get a card's ATK value
- `DLL_CardGetDef(card)`: Get a card's DEF value
- `DLL_CardGetLevel(card)`: Get a card's Level/Rank
- `DLL_CardGetAttr(card)`: Get a card's Attribute (DARK, LIGHT, etc.)
- `DLL_CardGetRace(card)`: Get a card's Race/Type (Warrior, Spellcaster, etc.)
- `DLL_CardGetType(card)`: Get a card's type (Monster, Spell, Trap, etc.)

## Card Type Functions

- `DLL_CardIsThisTrap(card)`: Check if a card is a Trap
- `DLL_CardIsThisTunerMonster(card)`: Check if a card is a Tuner
- `DLL_CardIsThisEffectMonster(card)`: Check if a card is an Effect Monster
- `DLL_CardIsThisMonster(card)`: Check if a card is any kind of Monster
- `DLL_CardIsThisSynchro(card)`: Check if a card is a Synchro Monster
- `DLL_CardIsThisXyz(card)`: Check if a card is an Xyz Monster

## Duel State Functions

- `DLL_DuelGetLP(duel, player)`: Get a player's Life Points
- `DLL_DuelGetCardNum(duel, player, location)`: Get the number of cards in a specific location
- `DLL_DuelGetCard(duel, player, location, sequence)`: Get a specific card from the field
- `DLL_DuelCanIDoSpecialSummon(duel)`: Check if the bot can Special Summon

## Action Functions

- `DLL_DuelComDoSummon(card)`: Check if a card can be Normal Summoned
- `DLL_DuelComDoActivate(card)`: Check if a card can be activated
- `DLL_DuelComDoSet(card)`: Check if a card can be Set
- `DLL_DuelComDoAttack(attacker, target)`: Make a card attack a specific target
- `DLL_DuelComDoDirectAttack(attacker)`: Make a card attack directly
- `DLL_DuelComDoEnd(executor)`: End the current phase

## AI Decision Functions

- `DLL_AISelectCard(cards, util)`: Select the best card from a list of options
- `DLL_AISelectYesNo()`: Select Yes or No (defaults to Yes)
- `DLL_AISelectOption(options)`: Select from multiple options (defaults to first option)

## Usage Example

```csharp
// Example of using these functions in an AI
ClientCard monster = DLL_DuelGetCard(duel, 0, CardLocation.Hand, 0);
if (monster != null && DLL_CardIsThisMonster(monster) && DLL_DuelComDoSummon(monster))
{
    // Summon the monster
}

// Check Life Points difference
int myLP = DLL_DuelGetLP(duel, 0);
int opponentLP = DLL_DuelGetLP(duel, 1);
if (myLP < opponentLP)
{
    // Take defensive actions
}
``` 