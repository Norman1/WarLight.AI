using System;
using System.Collections.Generic;
using WarLight.Shared.AI.Wunderwaffe.Bot;
using WarLight.Shared.AI.Wunderwaffe.Move;
using System.Linq;

namespace WarLight.Shared.AI.Wunderwaffe.Tasks
{
    public class ExpansionTask
    {
        public BotMain BotState;
        private List<BotBonus> takeOverBonuses = new List<BotBonus>();
        public List<BotBonus> expandBonuses = new List<BotBonus>();
        public List<BonusSpeedAnnotation> bonusSpeedAnnotations = new List<BonusSpeedAnnotation>();


        public ExpansionTask(BotMain state)
        {
            this.BotState = state;
        }

        public void CalculateExpansionMoves(Moves moves)
        {
            int armiesForExpansion = BotState.MyIncome.Total - moves.GetTotalDeployment();
            calculateBonusSpeedAnnotatoins(armiesForExpansion);
            AddValueForBonusSpeed(armiesForExpansion);
            CalculateTakeOverMoves(moves);
            CalculateNonTakeOverMoves(moves);
            CalculateNullTakeOverMoves(moves);
            annotateMissingTerritoryArmies();
        }

        public int getTerritoryAnnotationArmies(BotTerritory territory)
        {
            // -1 means that the annotation is missing
            int missingArmies = -1;
            foreach (BonusSpeedAnnotation bsa in bonusSpeedAnnotations)
            {
                foreach (TerritoryArmiesAnnotation territoriyArmiesAnnotation in bsa.territoryArmiesAnnotations)
                {
                    if (territoriyArmiesAnnotation.Territory.ID == territory.ID)
                    {
                        missingArmies = territoriyArmiesAnnotation.Armies;
                        break;
                    }
                }
            }
            return missingArmies;
        }

        private void annotateMissingTerritoryArmies()
        {
            foreach (BotBonus bonus in expandBonuses)
            {
                BonusSpeedAnnotation bsa = bonusSpeedAnnotations.Where(o => o.Bonus == bonus).First();
                List<BotTerritory> ownedTerritories = bonus.GetOwnedTerritoriesAndNeighbors();
                List<BotTerritory> alreadyHandledNeutralTerritories = new List<BotTerritory>();
                ownedTerritories = ownedTerritories.OrderByDescending(o => o.ExpansionTerritoryValue).ToList();
                foreach (BotTerritory ownedTerritory in ownedTerritories)
                {
                    // TODO, calculate correct number
                    int armiesAlreadyThere = 1;
                    List<BotTerritory> neutralNeighbors = ownedTerritory.GetNeighborsWithinSameBonus().Where(neighbor => neighbor.OwnerPlayerID == TerritoryStanding.NeutralPlayerID).ToList();
                    neutralNeighbors.RemoveWhere(o => alreadyHandledNeutralTerritories.Contains(o));
                    alreadyHandledNeutralTerritories.AddRange(neutralNeighbors);

                    int neededArmies = 0;
                    foreach (BotTerritory neighbor in neutralNeighbors)
                    {
                        neededArmies += neighbor.getNeededBreakArmies(neighbor.Armies.DefensePower);
                    }
                    neededArmies = Math.Max(0, neededArmies - armiesAlreadyThere);
                    if (neededArmies > 0)
                    {
                        TerritoryArmiesAnnotation taa = new TerritoryArmiesAnnotation(ownedTerritory, neededArmies);
                        bsa.territoryArmiesAnnotations.Add(taa);
                    }
                }
            }
        }

        private void CalculateNonTakeOverMoves(Moves moves)
        {
            var sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            int armiesForExpansion = BotState.MyIncome.Total - moves.GetTotalDeployment();
            bool isExpandingAfterTakeOverSmart = IsExpandingAfterTakeOverSmart();
            bool opponentBorderPresent = BotState.VisibleMap.GetOpponentBorderingTerritories().Count > 0;

            if (takeOverBonuses.Count == 0 || isExpandingAfterTakeOverSmart)
            {
                BotBonus bonusToExpand = null;
                foreach (var bonus in sortedAccessibleBonuses)
                {
                    if (!takeOverBonuses.Contains(bonus))
                    {
                        var condition1 = bonus.GetVisibleNeutralTerritories().Count > 0;
                        var condition2 = bonus.Amount > 0;
                        var condition3 = !opponentBorderPresent || bonus.ExpansionValueCategory > 0;
                        if (condition1 && condition2 && condition3)
                        {
                            bonusToExpand = bonus;
                            break;
                        }
                    }
                }
                if (bonusToExpand == null)
                {
                    return;
                }
                var foundMoves = true;
                var firstStep = true;
                while (foundMoves)
                {
                    BotState.BonusValueCalculator.CalculateBonusValues(BotState.WorkingMap, BotState.VisibleMap);
                    foundMoves = false;
                    if (firstStep == false)
                    {
                        if (bonusToExpand.ExpansionValueCategory == 0)
                            return;
                        if (opponentBorderPresent)
                            armiesForExpansion = 0;
                        if (bonusToExpand.GetOpponentNeighbors().Count > 0)
                            return;
                    }
                    Moves oneStepMoves = null;
                    if (!opponentBorderPresent)
                    {
                        oneStepMoves = BotState.TakeTerritoriesTaskCalculator.CalculateOneStepExpandBonusTask(armiesForExpansion, bonusToExpand, true, BotState.WorkingMap, BotTerritory.DeploymentType.Normal);

                    }
                    else
                    {
                        oneStepMoves = BotState.TakeTerritoriesTaskCalculator.CalculateOneStepExpandBonusTask(armiesForExpansion, bonusToExpand, false, BotState.WorkingMap, BotTerritory.DeploymentType.Normal);
                    }

                    if (oneStepMoves != null)
                    {
                        if (!expandBonuses.Contains(bonusToExpand))
                        {
                            expandBonuses.Add(bonusToExpand);
                        }
                        firstStep = false;
                        armiesForExpansion -= oneStepMoves.GetTotalDeployment();
                        MovesCommitter.CommittMoves(BotState, oneStepMoves);
                        moves.MergeMoves(oneStepMoves);
                        foundMoves = true;
                    }
                }
            }
        }

        private void CalculateNullTakeOverMoves(Moves moves)
        {
            var sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            var bonusesThatCanBeTaken = GetBonusesThatCanBeTaken(0);
            foreach (var bonus in sortedAccessibleBonuses)
            {
                if (bonusesThatCanBeTaken.Contains(bonus) && bonus.GetOpponentNeighbors().Count == 0)
                {
                    Moves expansionMoves = getTakeOverMoves(0, bonus);
                    MovesCommitter.CommittMoves(BotState, expansionMoves);
                    moves.MergeMoves(expansionMoves);
                    bonusesThatCanBeTaken = GetBonusesThatCanBeTaken(0);
                    takeOverBonuses.Add(bonus);
                }
            }
        }

        private void CalculateTakeOverMoves(Moves moves)
        {
            int armiesForExpansion = BotState.MyIncome.Total - moves.GetTotalDeployment();
            var sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            var bonusesThatCanBeTaken = GetBonusesThatCanBeTaken(armiesForExpansion);
            foreach (var bonus in sortedAccessibleBonuses)
            {
                if (bonusesThatCanBeTaken.Contains(bonus))
                {
                    Moves expansionMoves = getTakeOverMoves(armiesForExpansion, bonus);
                    MovesCommitter.CommittMoves(BotState, expansionMoves);
                    moves.MergeMoves(expansionMoves);
                    armiesForExpansion -= expansionMoves.GetTotalDeployment();
                    bonusesThatCanBeTaken = GetBonusesThatCanBeTaken(armiesForExpansion);
                    takeOverBonuses.Add(bonus);
                }
                else
                {
                    break;
                }
            }
        }


        private Moves getTakeOverMoves(int armiesForExpansion, BotBonus bonus)
        {
            Moves expansionMoves = BotState.TakeTerritoriesTaskCalculator.CalculateTakeTerritoriesTask(armiesForExpansion, bonus.GetNotOwnedTerritories(), BotTerritory.DeploymentType.Normal, "MovesCalculator.CalculateExpansionMoves");
            return expansionMoves;
        }


        private Boolean IsExpandingAfterTakeOverSmart()
        {
            Boolean isSmart = true;
            if (BotState.VisibleMap.GetOpponentBorderingTerritories().Count > 0)
            {
                isSmart = false;
            }
            if (BotState.WorkingMap.GetOpponentBorderingTerritories().Count > 0)
            {
                isSmart = false;
            }
            return isSmart;
        }




        private void AddValueForBonusSpeed(int maxDeployment)
        {
            foreach (BonusSpeedAnnotation bsa in bonusSpeedAnnotations)
            {
                BotState.BonusExpansionValueCalculator.AddExtraValueForSpeed(bsa.Bonus, bsa.Turns);
            }
            //var sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            //foreach (BotBonus bonus in sortedAccessibleBonuses)
            //{
            //    if ((bonus.AreAllTerritoriesVisible()) && (!bonus.ContainsOpponentPresence()) && (bonus.Amount > 0) && !bonus.IsOwnedByMyself())
            //    {
            //        var nonOwnedTerritories = bonus.GetNotOwnedTerritories();
            //        var expansionMoves = BotState.TakeTerritoriesTaskCalculator.CalculateTakeTerritoriesTask(maxDeployment, nonOwnedTerritories, BotTerritory.DeploymentType.Normal, "MovesCalculator.AddValueToImmediateBonuses");
            //        if (expansionMoves != null)
            //BotState.BonusExpansionValueCalculator.AddExtraValueForFirstTurnBonus(bonus);
            //    }
            //}
        }

        private void calculateBonusSpeedAnnotatoins(int maxDeployment)
        {
            List<BotBonus> bonusesThatCanBeTaken = GetBonusesThatCanBeTaken(maxDeployment);
            // Step 1: Handle the bonuses which can get immediately taken
            foreach (BotBonus bonus in bonusesThatCanBeTaken)
            {
                BonusSpeedAnnotation bsa = new BonusSpeedAnnotation(bonus, 1);
                bonusSpeedAnnotations.Add(bsa);
            }

            // Step 2: Estimate the turns to take the bonuses which can't get immediately taken
            List<BotBonus> sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            sortedAccessibleBonuses.RemoveAll(bonus => bonusesThatCanBeTaken.Contains(bonus));
            sortedAccessibleBonuses.RemoveAll(bonus => bonus.ContainsOpponentPresence() || bonus.NeutralTerritories.Count == 0);

            foreach (BotBonus bonus in sortedAccessibleBonuses)
            {
                if (maxDeployment == 0)
                {
                    bonusSpeedAnnotations.Add(new BonusSpeedAnnotation(bonus, 1000));
                }
                else
                {
                    int maxDistance = GetMaxBonusDistance(bonus);
                    int availableAttackArmies = GetAlreadyFloatingArmies(bonus);
                    int neutralKills = GetNeutralKills(bonus);
                    int turnsToTake = 2;
                    while (availableAttackArmies + turnsToTake * maxDeployment < neutralKills)
                    {
                        turnsToTake++;
                    }
                    turnsToTake = Math.Max(turnsToTake, maxDistance);
                    bonusSpeedAnnotations.Add(new BonusSpeedAnnotation(bonus, turnsToTake));
                }

            }
        }

        private int GetAlreadyFloatingArmies(BotBonus bonus)
        {
            List<BotTerritory> nearbyTerritories = bonus.GetOwnedTerritoriesAndNeighbors();
            int armiesCount = 0;
            foreach (BotTerritory territory in nearbyTerritories)
            {
                armiesCount = armiesCount + territory.GetArmiesAfterDeploymentAndIncomingMoves().DefensePower - 1;
            }
            return Math.Max(armiesCount, 0); ;
        }

        private int GetNeutralKills(BotBonus bonus)
        {
            int neutralKills = 0;
            List<BotTerritory> neutralTerritories = bonus.NeutralTerritories;
            foreach (BotTerritory neutralTerritory in neutralTerritories)
            {
                // one army must stand guard
                neutralKills += 1;
                neutralKills += (int)Math.Round(neutralTerritory.Armies.DefensePower * BotState.Settings.DefenseKillRate);
            }
            return neutralKills;
        }

        private int GetMaxBonusDistance(BotBonus bonus)
        {
            List<BotTerritory> neutralTerritories = bonus.NeutralTerritories;
            return neutralTerritories.OrderByDescending(o => o.DirectDistanceToOwnBorder).First().DirectDistanceToOwnBorder;
        }


        private List<BotBonus> GetBonusesThatCanBeTaken(int maxDeployment)
        {
            var outvar = new List<BotBonus>();
            var sortedAccessibleBonuses = BotState.BonusExpansionValueCalculator.SortAccessibleBonuses(BotState.VisibleMap);
            foreach (var bonus in sortedAccessibleBonuses)
            {
                if (bonus.AreAllTerritoriesVisible() && !bonus.ContainsOpponentPresence() && bonus.Amount > 0)
                {
                    var nonOwnedTerritories = bonus.GetNotOwnedTerritories();
                    var expansionMoves = BotState.TakeTerritoriesTaskCalculator.CalculateTakeTerritoriesTask(maxDeployment, nonOwnedTerritories, BotTerritory.DeploymentType.Normal, "MovesCalculator.GetBonusesThatCanBeTaken");
                    if (expansionMoves != null)
                        outvar.Add(bonus);
                }
            }
            outvar.RemoveAll(o => takeOverBonuses.Contains(o));
            outvar.RemoveAll(o => expandBonuses.Contains(o));
            return outvar;
        }

        public class BonusSpeedAnnotation
        {
            public BotBonus Bonus = null;
            public int Turns = -1;
            public List<TerritoryArmiesAnnotation> territoryArmiesAnnotations = new List<TerritoryArmiesAnnotation>();
            public BonusSpeedAnnotation(BotBonus bonus, int turns)
            {
                this.Bonus = bonus;
                this.Turns = turns;
            }
        }

        public class TerritoryArmiesAnnotation
        {
            public TerritoryArmiesAnnotation(BotTerritory territory, int armies)
            {
                this.Territory = territory;
                this.Armies = armies;
            }
            public BotTerritory Territory;
            public int Armies;
        }

    }
}