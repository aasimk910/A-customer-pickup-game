using UnityEngine;

/// <summary>
/// Heuristic calculator for A* pathfinding (Euclidean distance).
/// </summary>
public class Heuristic
{
    public Heuristic()
    {
    }

    public float Estimate(GameObject startNode, GameObject goalNode)
    {
        return Vector3.Distance(startNode.transform.position, goalNode.transform.position);
    }
}