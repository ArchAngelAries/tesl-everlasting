using System;
using SPTESL;

namespace OfflineEngine
{
    public static class NativeHistoryFx
    {
        // SequenceCardPlay consumes the Action/Event AssetHash directly. Its
        // AddCardAnimating is cleared by the original placement VisualDirector;
        // a missing definition leaves the original card stuck in animation.
        public static int PlayHash(ServerCard card)
        {
            if (card == null) throw new ArgumentNullException("card");
            CardFxAction action;
            if (card.HasSubtype(CardSubtype.Creature)) action = CardFxAction.PlayCard;
            else if (card.HasSubtype(CardSubtype.Item)) action = CardFxAction.PlayCardItem;
            else if (card.HasSubtype(CardSubtype.Spell)) action = CardFxAction.PlayCardAction;
            else if (card.HasSubtype(CardSubtype.Support)) action = CardFxAction.PlayCardSupport;
            else throw new NotSupportedException("Native placement FX route is not verified for card " + card.CardId + ".");
            int hash = card.FindOverrideOrDefaultAssetHash(action);
            GameConfiguration config = GameManager.Instance.Config;
            string name;
            if (hash == -1 || config.CardFxDefinitionLookups == null
                || !config.CardFxDefinitionLookups.TryGetValue(hash, out name)
                || config.CardFxDefinitions == null
                || config.CardFxDefinitions.Find(delegate(CardFxDefinition definition) { return definition.Name == name; }) == null)
                throw new NotSupportedException("Native original placement FX definition is missing for card " + card.CardId + " / " + action + ".");
            return hash;
        }
    }
}
