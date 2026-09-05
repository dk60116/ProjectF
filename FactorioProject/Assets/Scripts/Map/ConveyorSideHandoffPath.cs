using UnityEngine;

namespace ProjectF.Conveyors
{
    internal static class ConveyorSideHandoffPath
    {
        internal static Vector3 GetApproachPosition(Vector3 sourceFront, Vector2Int incomingFlow, float spacing)
        {
            return sourceFront + new Vector3(incomingFlow.x, 0f, incomingFlow.y) * spacing;
        }

        internal static bool IsSideEntry(Vector2Int incomingStep, Vector2Int destinationFlow)
        {
            return Mathf.Abs(incomingStep.x) + Mathf.Abs(incomingStep.y) == 1
                && Mathf.Abs(destinationFlow.x) + Mathf.Abs(destinationFlow.y) == 1
                && incomingStep.x * destinationFlow.x + incomingStep.y * destinationFlow.y == 0;
        }

        internal static Vector3 GetTurnPosition(Vector3 start, Vector3 destination, Vector2Int destinationFlow)
        {
            // Intersect the incoming line with the receiving belt's centerline.
            // The reserved slot must be downstream of this turn, never behind it.
            Vector3 flow = new Vector3(destinationFlow.x, 0f, destinationFlow.y);
            Vector3 turn = destination - flow * Vector3.Dot(destination - start, flow);
            turn.y = start.y;
            return turn;
        }
    }
}
