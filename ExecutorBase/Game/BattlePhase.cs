﻿using System.Collections.Generic;

namespace WindBot.Game
{
    public class BattlePhase
    {
        public IList<ClientCard> AttackableCards { get; private set; }
        public IList<ClientCard> ActivableCards { get; private set; }
        public IList<long> ActivableDescs { get; private set; }
        public bool CanMainPhaseTwo { get; set; }
        public bool CanEndPhase { get; set; }
        public bool End { get; set; }

        public BattlePhase()
        {
            AttackableCards = new List<ClientCard>();
            ActivableCards = new List<ClientCard>();
            ActivableDescs = new List<long>();
        }
    }
}