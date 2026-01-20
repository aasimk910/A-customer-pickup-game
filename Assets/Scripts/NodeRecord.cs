using UnityEngine;

/// <summary>
/// Node record for A* pathfinding algorithm.
/// </summary>
public class NodeRecord
{
    #region Properties
    
    public GameObject Node { get; set; }
    public Connection Connection { get; set; }
    public float CostSoFar { get; set; }
    public float EstimatedTotalCost { get; set; }
    
    #endregion

    #region Constructor

    public NodeRecord()
    {
    }
    
    #endregion
}
