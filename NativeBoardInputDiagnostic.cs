using System;
using System.Reflection;
using CcgRpc;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Diagnostic synthetic pointer samples enter the shipped input state machine.
    // This proves its command/drag handlers, not physical OS mouse operation.
    public static class NativeBoardInputDiagnostic
    {
        private static BoardInputController.RawInput? pending;
        private static readonly FieldInfo commands = AccessTools.Field(typeof(BoardInputController), "_commandList");
        private static readonly FieldInfo state = AccessTools.Field(typeof(BoardInputController), "_inputState");

        public static void Install(Harmony harmony)
        {
            RequireDiagnostic();
            harmony.Patch(AccessTools.Method(typeof(BoardInputController), "CollectRawInput"),
                new HarmonyMethod(typeof(NativeBoardInputDiagnostic), "CollectPrefix"), null, null, null, null);
        }

        private static void RequireDiagnostic()
        {
            if (!OfflineBootstrapPlugin.Active || Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-diagnostic") < 0)
                throw new InvalidOperationException("Native input samples require the explicit offline board diagnostic.");
        }

        private static bool CollectPrefix(ref BoardInputController.RawInput __result)
        {
            if (!pending.HasValue) return true;
            __result = pending.Value;
            pending = null;
            return false;
        }

        public static CardCommandList ClientCommands(BoardConductor board)
        {
            BoardInputController input = board.GetComponent<BoardInputController>();
            return input == null ? null : (CardCommandList)commands.GetValue(input);
        }

        public static bool IsReady(BoardConductor board, int expectedUid)
        {
            BoardInputController input = board.GetComponent<BoardInputController>();
            CardCommandList list = ClientCommands(board);
            return input != null && input.enabled && input.gameObject.activeInHierarchy && list != null && list.ListUid == expectedUid
                && (bool)AccessTools.Method(typeof(BoardInputController), "CanLocalPlayerTakeAction").Invoke(input, null);
        }

        public static void EndTurn(BoardConductor board)
        {
            RequireDiagnostic();
            BoardInputController input = board.GetComponent<BoardInputController>();
            CardCommandList list = ClientCommands(board);
            if (list == null || !IsReady(board, list.ListUid)) throw new InvalidOperationException("Original input controller is not ready.");
            input.OnButtonActionClicked(BoardInputController.InputCommandType.Action_EndTurn);
            if ((BoardInputController.InputCommandType)AccessTools.Field(typeof(BoardInputController), "_buttonCommand").GetValue(input)
                != BoardInputController.InputCommandType.Action_EndTurn)
                throw new InvalidOperationException("Original end-turn button rejected the local command list.");
            // The next ordinary Unity Update consumes this accepted button command.
        }

        public static int PlayThroughDrag(BoardConductor board, int uid, int laneIndex)
        {
            RequireDiagnostic();
            BoardInputController input = board.GetComponent<BoardInputController>();
            CardCommandList list = ClientCommands(board);
            if (list == null || !IsReady(board, list.ListUid)) throw new InvalidOperationException("Original board drag input is not ready.");
            if (Singleton<SettingsManager>.Instance.UseTouchInput || Singleton<SettingsManager>.Instance.UseGamepad)
                throw new InvalidOperationException("This bounded pointer diagnostic requires the native desktop input mode.");
            Card3D card = board.BoardState.FindCardByNetUid(uid);
            if (card == null || card.Animating || card.ClientCard.GameZoneName != GameZoneName.Hand)
                throw new InvalidOperationException("Native client hand card is absent or still animating: uid " + uid + (card == null ? " absent" : " animating " + card.Animating + ", zone " + (card.ClientCard == null ? "none" : card.ClientCard.GameZoneName.ToString())) + ".");
            IClientGameZone lane = board.BoardState.FindLane(GameZoneName.CreatureBoard, PlayerControllerType.Player,
                laneIndex == 0 ? ClientGameZonePosition.Left : ClientGameZonePosition.Right);
            if (lane == null) throw new InvalidOperationException("Native target lane is absent.");
            Vector3 destination = lane.BoundsDefinition.Definition.center;
            BoardContext context = board.BoardState.QueryBoardContext(destination);
            if (!(laneIndex == 0 ? context.overMyLaneLeft : context.overMyLaneRight))
                throw new InvalidOperationException("Native lane bounds did not identify their own center.");
            int position = laneIndex == 0 ? context.positionIndexMyLaneLeft : context.positionIndexMyLaneRight;
            Drag(input, card, destination, null, "HandFocus");
            return position;
        }

        public static void AttackThroughDrag(BoardConductor board, int actorUid, int targetUid)
        {
            RequireDiagnostic();
            BoardInputController input = board.GetComponent<BoardInputController>();
            CardCommandList list = ClientCommands(board);
            if (list == null || !IsReady(board, list.ListUid)) throw new InvalidOperationException("Native attack input is not ready.");
            Card3D actor = board.BoardState.FindCardByNetUid(actorUid), target = board.BoardState.FindCardByNetUid(targetUid);
            if (actor == null || target == null || actor.Animating || target.Animating || actor.ClientCard.GameZoneName != GameZoneName.CreatureBoard)
                throw new InvalidOperationException("Native attack cards are absent or animating.");
            Drag(input, actor, target.transform.position, target, "BoardFocus");
        }

        private static void Drag(BoardInputController input, Card3D card, Vector3 destination, Card3D target, string focus)
        {
            BoardInputController.RawInput raw = new BoardInputController.RawInput();
            raw.inputType = Singleton<SettingsManager>.Instance.CurrentInputType;
            raw.inputCamera = input.InputCamera;
            raw.orthoInputCamera = input.OrthoInputCamera;
            raw.uiCamera = input.UICamera;
            raw.cardUnderPointer = card;
            raw.cardUnderPointerId = card.NetUid;
            raw.worldPosition = card.transform.position;
            raw.screenPosition = input.InputCamera.WorldToScreenPoint(card.transform.position);
            raw.onDownInput = new BoardInputController.RawOnDownInput { frameTime = Time.time, screenPosition = raw.screenPosition, cardUnderPointer = card };
            raw.touchEvent = BoardInputController.TouchEventType.None;
            Step(input, raw);
            AssertState(input, focus);
            raw.touchEvent = BoardInputController.TouchEventType.Held;
            Step(input, raw);
            AssertState(input, "Dragging");
            raw.lastTouchEvent = BoardInputController.TouchEventType.Held;
            raw.touchEvent = BoardInputController.TouchEventType.Up_Slow;
            raw.worldPosition = destination;
            raw.dragCardPosition = destination;
            raw.screenPosition = input.InputCamera.WorldToScreenPoint(destination);
            raw.cardUnderPointer = target;
            raw.cardUnderPointerId = target == null ? CardNetUid.GetInvalid() : target.NetUid;
            Step(input, raw);
            AssertState(input, "None");
            if (commands.GetValue(input) != null) throw new InvalidOperationException("Native drag did not consume its client command list.");
        }

        private static void Step(BoardInputController input, BoardInputController.RawInput raw)
        {
            pending = raw;
            try
            {
                AccessTools.Method(typeof(BoardInputController), "InputUpdate").Invoke(input, null);
                if (pending.HasValue) throw new InvalidOperationException("Native input update did not collect its pointer sample.");
            }
            finally { pending = null; }
        }

        private static void AssertState(BoardInputController input, string expected)
        {
            object value = state.GetValue(input);
            string actual = value.GetType().GetField("State").GetValue(value).ToString();
            if (actual != expected) throw new InvalidOperationException("Native pointer state expected " + expected + ", got " + actual + ".");
        }
    }
}
