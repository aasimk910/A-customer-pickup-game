using UnityEngine;

/// <summary>
/// Heuristic calculator for A* pathfinding (Euclidean distance).
/// </summary>
public class Heuristic
{
    #region Constructor
    
    public Heuristic()
    {
    }
    
    #endregion

    #region Public Methods

    public float Estimate(GameObject startNode, GameObject goalNode)
    {
        return Vector3.Distance(startNode.transform.position, goalNode.transform.position);
    }
    
    #endregion
}