using System.Collections.Generic;
using YGOSharp.OCGWrapper.Enums;
using WindBot.Game;
using WindBot.Game.AI;

namespace WindBot.Game
{
    public static class DLL
    {
        public static int DLL_CardGetAtk(ClientCard card)
        {
            return card?.Attack ?? 0;
        }

        public static int DLL_CardGetDef(ClientCard card)
        {
            return card?.Defense ?? 0;
        }

        public static int DLL_CardGetLevel(ClientCard card)
        {
            return card?.Level ?? 0;
        }

        public static CardAttribute DLL_CardGetAttr(ClientCard card)
        {
            if (card != null && card.Attribute != 0)
                return (CardAttribute)card.Attribute;
            return CardAttribute.Earth; // Default to Earth if null
        }

        public static CardRace DLL_CardGetRace(ClientCard card)
        {
            if (card != null && card.Race != 0)
                return (CardRace)card.Race;
            return CardRace.Warrior; // Default to Warrior if null
        }

        public static CardType DLL_CardGetType(ClientCard card)
        {
            if (card != null && card.Type != 0)
                return (CardType)card.Type;
            return CardType.Monster; // Default to Monster if null
        }

        public static bool DLL_CardIsThisTrap(ClientCard card)
        {
            return card != null && card.HasType(CardType.Trap);
        }

        public static bool DLL_CardIsThisTunerMonster(ClientCard card)
        {
            return card != null && card.HasType(CardType.Tuner);
        }

        public static bool DLL_CardIsThisEffectMonster(ClientCard card)
        {
            return card != null && card.HasType(CardType.Effect);
        }

        public static bool DLL_CardIsThisMonster(ClientCard card)
        {
            return card != null && card.HasType(CardType.Monster);
        }

        public static bool DLL_CardIsThisSynchro(ClientCard card)
        {
            return card != null && card.HasType(CardType.Synchro);
        }

        public static bool DLL_CardIsThisXyz(ClientCard card)
        {
            return card != null && card.HasType(CardType.Xyz);
        }

        public static int DLL_DuelGetLP(Duel duel, int player)
        {
            return duel.Fields[player].LifePoints;
        }

        public static int DLL_DuelGetCardNum(Duel duel, int player, CardLocation location)
        {
            ClientField field = duel.Fields[player];
            
            switch (location)
            {
                case CardLocation.Hand:
                    return field.Hand.Count;
                case CardLocation.MonsterZone:
                    return field.GetMonsterCount();
                case CardLocation.SpellZone:
                    return field.GetSpellCount();
                case CardLocation.Grave:
                    return field.Graveyard.Count;
                case CardLocation.Deck:
                    return field.Deck.Count;
                case CardLocation.Extra:
                    return field.ExtraDeck.Count;
                case CardLocation.Removed:
                    return field.Banished.Count;
                default:
                    return 0;
            }
        }

        public static ClientCard DLL_DuelGetCard(Duel duel, int player, CardLocation location, int sequence)
        {
            return duel.GetCard(player, location, sequence);
        }

        public static bool DLL_DuelCanIDoSpecialSummon(Duel duel)
        {
            foreach (ClientCard card in duel.Fields[0].Hand)
            {
                if (card.HasType(CardType.Monster))
                    return true;
            }
            
            return false;
        }

        public static bool DLL_DuelComDoSummon(ClientCard card)
        {
            return card != null && card.HasType(CardType.Monster) && card.Location == CardLocation.Hand;
        }

        public static bool DLL_DuelComDoActivate(ClientCard card)
        {
            return card != null && (card.IsFaceup() || card.Location == CardLocation.Hand);
        }

        public static bool DLL_DuelComDoSet(ClientCard card)
        {
            return card != null && card.Location == CardLocation.Hand;
        }

        public static bool DLL_DuelComDoAttack(ClientCard attacker, ClientCard target)
        {
            if (attacker == null || !attacker.HasType(CardType.Monster) || !attacker.IsAttack())
                return false;
                
            if (target != null && target.HasType(CardType.Monster))
                return true;
                
            return false;
        }

        public static bool DLL_DuelComDoDirectAttack(ClientCard attacker)
        {
            return attacker != null && attacker.HasType(CardType.Monster) && attacker.IsAttack();
        }

        public static void DLL_DuelComDoEnd(Executor executor)
        {
            executor.SetEnd();
        }

        public static ClientCard DLL_AISelectCard(List<ClientCard> cards, AIUtil util)
        {
            return util.SelectBestCard(cards);
        }

        public static bool DLL_AISelectYesNo()
        {
            return true;
        }

        public static int DLL_AISelectOption(params int[] options)
        {
            return options.Length > 0 ? options[0] : -1;
        }
    }
} 