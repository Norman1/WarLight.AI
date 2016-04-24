using System.Linq;
using WarLight.Shared.AI.Wunderwaffe.Bot;
using WarLight.Shared.AI.Wunderwaffe.Move;
using WarLight.Shared.AI.Wunderwaffe.Bot.Cards;
using System.Collections.Generic;

namespace WarLight.Shared.AI.Wunderwaffe.Tasks
{
    public static class PlayCardsTask
    {
        public static void PlayCardsBeginTurn(BotMain state, Moves moves)
        {
            //If there are any humans on our team that have yet to take their turn, do not play cards.
            if (state.Me.Team != PlayerInvite.NoTeam && state.Players.Values.Any(o => state.IsTeammate(o.ID) && !o.IsAIOrHumanTurnedIntoAI && o.State == GamePlayerState.Playing && !o.HasCommittedOrders))
            {
                return;
            }

            foreach (var reinforcementCard in state.CardsHandler.GetCards(CardTypes.Reinforcement))
            {
                var numArmies = reinforcementCard.As<ReinforcementCard>().Armies;
                AILog.Log("PlayCardsTask", "Playing reinforcement card " + reinforcementCard.CardInstanceId + " for " + numArmies + " armies");
                moves.AddOrder(new BotOrderGeneric(GameOrderPlayCardReinforcement.Create(reinforcementCard.CardInstanceId, state.Me.ID)));
                state.MyIncome.FreeArmies += numArmies;
            }


            foreach (Card sanctionsCard in state.CardsHandler.GetCards(CardTypes.Sanctions))
            {
                List<GamePlayer> opponents = state.Opponents.ToList();
                opponents.OrderByDescending(o => state.GetGuessedOpponentIncome(o.ID, state.VisibleMap));
                AILog.Log("PlayCardsTask", "Playing sanctions card");
                // TODO sanctions card can have negative percentages in which case we don't want to sanction our opponent
                moves.AddOrder(new BotOrderGeneric(GameOrderPlayCardSanctions.Create(sanctionsCard.CardInstanceId, state.Me.ID, opponents[0].ID)));
            }

            foreach (Card bombCard in state.CardsHandler.GetCards(CardTypes.Bomb))
            {
                List<BotTerritory> opponentTerritories = state.VisibleMap.GetVisibleOpponentTerritories().Where(t => t.GetOwnedNeighbors().Count > 0).ToList();
                opponentTerritories.OrderByDescending(t => t.Armies.DefensePower);
                opponentTerritories.OrderByDescending(t => t.GetArmiesAfterDeployment(BotTerritory.DeploymentType.Normal));
                if (opponentTerritories.Count > 0)
                {
                    int armies = opponentTerritories[0].GetArmiesAfterDeployment(BotTerritory.DeploymentType.Normal).DefensePower;
                    // kinda random heuristic
                    if (armies >= 3)
                    {
                        AILog.Log("PlayCardsTask", "Playing bomb card");
                        moves.AddOrder(new BotOrderGeneric(GameOrderPlayCardBomb.Create(bombCard.CardInstanceId, state.Me.ID, opponentTerritories[0].ID)));
                        opponentTerritories[0].amountBombed++;
                    }
                }
            }


        }

        public static void DiscardCardsEndTurn(BotMain state, Moves moves)
        {

            //If there are players on our team that have yet to take their turn, do not discard cards
            if (state.Me.Team != PlayerInvite.NoTeam && state.Players.Values.Any(o => state.IsTeammate(o.ID) && o.State == GamePlayerState.Playing && !o.HasCommittedOrders))
            {
                return;
            }

            // Discard as many cards as needed
            var cardsWePlayed = moves.Convert().OfType<GameOrderPlayCard>().Select(o => o.CardInstanceID).ToHashSet(true);
            var cardsPlayedByAnyone = state.CardsPlayedByTeammates.Concat(cardsWePlayed).ToHashSet(true);

            int numMustPlay = state.CardsMustPlay;

            foreach (var card in state.Cards)
            {
                if (numMustPlay > 0 && !cardsPlayedByAnyone.Contains(card.ID))
                {
                    AILog.Log("PlayCardsTask", "Discarding card " + card.ID);
                    moves.AddOrder(new BotOrderGeneric(GameOrderDiscard.Create(state.Me.ID, card.ID)));
                    numMustPlay--;
                }
            }

        }
    }
}
