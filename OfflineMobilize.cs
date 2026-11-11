using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed Mobilize (Alliance War keyword). The shipped client stubs ServerCardCatalog.GatherMobilizeLaneTargets
    // (returns null) and has no server play orchestration. Rules from native PlayCardHandler validation + UESP Legends:
    // an item with Mobilize may be played to one of your lanes with no friendly creature; a 1/1 Recruit is summoned
    // there and the item is equipped to it. Card.MobilizeRecruitId names the recruit when set; installed Mobilize items
    // leave it empty, so the native RandomFromSelector pool for the Recruit subtype (the ten race Recruits) is used.
    public static class OfflineMobilize
    {
        public static List<BoardLaneTarget> LaneTargets(ServerCardCatalog catalog, ServerCard item, PlayerState player)
        {
            List<BoardLaneTarget> lanes = new List<BoardLaneTarget>();
            if (item.QueryAttribute(AttributeType.Mobilize).Value <= 0 || !item.HasSubtype(CardSubtype.Item)) return lanes;
            int count = catalog.FindLaneCount(GameZoneName.CreatureBoard, player.NetId);
            for (int lane = 0; lane < count && lane < 2; lane++)
            {
                int index = lane;
                if (catalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CurrentLaneIndex == index; }, player.NetId, GameZoneName.CreatureBoard) == null)
                    lanes.Add(lane == 0 ? BoardLaneTarget.MyLeftLane : BoardLaneTarget.MyRightLane);
            }
            return lanes;
        }

        public static ServerCard SummonRecruit(Simulation s, ServerEffectUtilities.TriggerGameState gs, PlayerState player, ServerCard item, BoardLaneTarget target)
        {
            int lane = target == BoardLaneTarget.MyLeftLane ? 0 : 1;
            int recruitId = RecruitId(s, GameManager.Instance.GetCard(item.CardId));
            ServerCard recruit = ServerCardCatalog.CreateCard(gs, recruitId, false, GameZoneName.CreatureBoard, lane, CardInsertionType.Bottom, player.NetId, null, null);
            if (recruit == null) throw new System.InvalidOperationException("Mobilize Recruit creation failed: " + recruitId);
            ServerEffectUtilities.TriggerWhenCardEntersBoard(gs, recruit); // also fires the Recruit's Summon (OfflineSummonRestoration)
            return recruit;
        }

        private static int RecruitId(Simulation s, Card item)
        {
            if (item != null && item.MobilizeRecruitId != null && item.MobilizeRecruitId.GetSpecificCardId() != UniqueResourceId.GetInvalid().Id)
                return item.MobilizeRecruitId.GetSpecificCardId();
            List<ICollectionConditionalExpression> selectors = new List<ICollectionConditionalExpression> {
                new CollectionConditionals.Subtype { Check = CollectionConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Recruit } };
            Card recruit = GameManager.Instance.GetRandomCardFromSelectorPool(selectors, s.RandomGenerator, default(CardConditionalArguments),
                default(PlayerConditionalArguments), default(TriggerConditionalArguments), new List<int>());
            if (recruit == null) throw new System.InvalidOperationException("No installed Recruit is available for Mobilize.");
            return recruit.ResourceId.Id;
        }
    }
}

